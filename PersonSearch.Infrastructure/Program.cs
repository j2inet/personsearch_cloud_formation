using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonSearch.Infrastructure;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});
services.AddSingleton<IConfiguration>(configuration);
services.AddTransient<CloudFormationDeployer>();
services.AddTransient<DatabaseInitializer>();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

var awsConfig = configuration.GetSection("AWS");
var region = awsConfig["Region"] ?? "us-east-1";
var stackName = awsConfig["StackName"] ?? "PersonSearch";
var environment = awsConfig["Environment"] ?? "dev";
var lambdaPackagePath = awsConfig["LambdaPackagePath"] ?? string.Empty;

logger.LogInformation("PersonSearch CloudFormation Deployer");
logger.LogInformation("Region: {Region}", region);
logger.LogInformation("Stack: {StackName}-{Environment}", stackName, environment);

if (args.Length > 0 && args[0].Equals("delete", StringComparison.OrdinalIgnoreCase))
{
    logger.LogInformation("Deleting stack {StackName}-{Environment}...", stackName, environment);
    var deployer = serviceProvider.GetRequiredService<CloudFormationDeployer>();
    await deployer.DeleteStackAsync($"{stackName}-{environment}", region);
    logger.LogInformation("Stack deletion initiated.");
    return;
}

if (args.Length > 0 && args[0].Equals("init-db", StringComparison.OrdinalIgnoreCase))
{
    logger.LogInformation("Initializing database schema...");
    var dbInit = serviceProvider.GetRequiredService<DatabaseInitializer>();
    var dsqlEndpoint = args.Length > 1 ? args[1] : string.Empty;
    if (string.IsNullOrEmpty(dsqlEndpoint))
    {
        logger.LogError("Please provide the DSQL endpoint as the second argument.");
        return;
    }
    await dbInit.InitializeSchemaAsync(dsqlEndpoint, region);
    logger.LogInformation("Database schema initialized.");
    return;
}

logger.LogInformation("Deploying PersonSearch infrastructure...");

try
{
    var deployer = serviceProvider.GetRequiredService<CloudFormationDeployer>();
    var fullStackName = $"{stackName}-{environment}";

    // Upload Lambda package if path provided
    string lambdaS3Key = "PersonSearch.Lambda.zip";
    string? s3Bucket = awsConfig["LambdaDeploymentBucket"];
    if (!string.IsNullOrEmpty(lambdaPackagePath) && File.Exists(lambdaPackagePath))
    {
        logger.LogInformation("Uploading Lambda package from {Path}...", lambdaPackagePath);
        s3Bucket = await deployer.UploadLambdaPackageAsync(lambdaPackagePath, fullStackName, lambdaS3Key, region, environment);
        logger.LogInformation("Lambda package uploaded to s3://{Bucket}/{Key}", s3Bucket, lambdaS3Key);
    }

    var outputs = await deployer.DeployStackAsync(fullStackName, environment, s3Bucket ?? string.Empty, lambdaS3Key, region);

    logger.LogInformation("Deployment complete. Stack outputs:");
    foreach (var output in outputs)
    {
        logger.LogInformation("  {Key} = {Value}", output.Key, output.Value);
    }

    // Initialize the database schema after deployment
    if (outputs.TryGetValue("DsqlClusterEndpoint", out var endpoint) && !string.IsNullOrEmpty(endpoint))
    {
        logger.LogInformation("Initializing database schema at {Endpoint}...", endpoint);
        var dbInit = serviceProvider.GetRequiredService<DatabaseInitializer>();
        await dbInit.InitializeSchemaAsync(endpoint, region);
        logger.LogInformation("Database schema initialized successfully.");
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Deployment failed");
    Environment.Exit(1);
}
