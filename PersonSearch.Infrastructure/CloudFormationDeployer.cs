using Amazon;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;

namespace PersonSearch.Infrastructure;

/// <summary>
/// Deploys the PersonSearch CloudFormation stack, which creates an Aurora DSQL cluster,
/// a Lambda function, IAM roles, and supporting resources.
/// </summary>
public class CloudFormationDeployer
{
    private readonly ILogger<CloudFormationDeployer> _logger;
    private const string TemplatePath = "Templates/infrastructure.yaml";

    public CloudFormationDeployer(ILogger<CloudFormationDeployer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Uploads the Lambda deployment package to S3 and returns the bucket name.
    /// </summary>
    public async Task<string> UploadLambdaPackageAsync(
        string packagePath, string stackName, string s3Key, string region)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        using var s3Client = new AmazonS3Client(regionEndpoint);

        // Derive a deterministic bucket name from the stack and account
        var sts = new Amazon.SecurityToken.AmazonSecurityTokenServiceClient(regionEndpoint);
        var identity = await sts.GetCallerIdentityAsync(new Amazon.SecurityToken.Model.GetCallerIdentityRequest());
        var bucketName = $"personsearch-lambda-{identity.Account}-{region}";

        // Create the bucket if it does not exist
        try
        {
            await s3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucketName,
                UseClientRegion = true
            });
            _logger.LogInformation("Created S3 bucket {Bucket}", bucketName);
        }
        catch (AmazonS3Exception ex) when (
            ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            _logger.LogInformation("S3 bucket {Bucket} already exists", bucketName);
        }

        using var transferUtility = new TransferUtility(s3Client);
        await transferUtility.UploadAsync(packagePath, bucketName, s3Key);
        return bucketName;
    }

    /// <summary>
    /// Creates or updates the CloudFormation stack and waits for it to reach a stable state.
    /// Returns a dictionary of stack output key/value pairs.
    /// </summary>
    public async Task<Dictionary<string, string>> DeployStackAsync(
        string stackName, string environment, string lambdaBucket, string lambdaKey, string region)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        using var cfClient = new AmazonCloudFormationClient(regionEndpoint);

        var templateBody = await File.ReadAllTextAsync(TemplatePath);

        var parameters = new List<Parameter>
        {
            new() { ParameterKey = "Environment",       ParameterValue = environment },
            new() { ParameterKey = "LambdaS3Bucket",    ParameterValue = lambdaBucket },
            new() { ParameterKey = "LambdaS3Key",       ParameterValue = lambdaKey }
        };

        var capabilities = new List<string> { "CAPABILITY_IAM", "CAPABILITY_NAMED_IAM" };

        bool stackExists = await StackExistsAsync(cfClient, stackName);

        if (stackExists)
        {
            _logger.LogInformation("Updating existing stack {StackName}...", stackName);
            try
            {
                await cfClient.UpdateStackAsync(new UpdateStackRequest
                {
                    StackName = stackName,
                    TemplateBody = templateBody,
                    Parameters = parameters,
                    Capabilities = capabilities
                });
            }
            catch (AmazonCloudFormationException ex) when (ex.Message.Contains("No updates are to be performed"))
            {
                _logger.LogInformation("No changes detected in stack {StackName}.", stackName);
                return await GetStackOutputsAsync(cfClient, stackName);
            }

            await WaitForStackOperationAsync(cfClient, stackName, "UPDATE_COMPLETE");
        }
        else
        {
            _logger.LogInformation("Creating new stack {StackName}...", stackName);
            await cfClient.CreateStackAsync(new CreateStackRequest
            {
                StackName = stackName,
                TemplateBody = templateBody,
                Parameters = parameters,
                Capabilities = capabilities,
                OnFailure = OnFailure.ROLLBACK
            });

            await WaitForStackOperationAsync(cfClient, stackName, "CREATE_COMPLETE");
        }

        return await GetStackOutputsAsync(cfClient, stackName);
    }

    /// <summary>
    /// Initiates deletion of the named CloudFormation stack.
    /// </summary>
    public async Task DeleteStackAsync(string stackName, string region)
    {
        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        using var cfClient = new AmazonCloudFormationClient(regionEndpoint);
        await cfClient.DeleteStackAsync(new DeleteStackRequest { StackName = stackName });
        await WaitForStackOperationAsync(cfClient, stackName, "DELETE_COMPLETE");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<bool> StackExistsAsync(AmazonCloudFormationClient client, string stackName)
    {
        try
        {
            var response = await client.DescribeStacksAsync(new DescribeStacksRequest { StackName = stackName });
            return response.Stacks.Count > 0;
        }
        catch (AmazonCloudFormationException ex) when (ex.ErrorCode == "ValidationError")
        {
            return false;
        }
    }

    private async Task WaitForStackOperationAsync(
        AmazonCloudFormationClient client, string stackName, string successStatus)
    {
        const int pollIntervalSeconds = 10;
        const int maxWaitMinutes = 30;
        var deadline = DateTime.UtcNow.AddMinutes(maxWaitMinutes);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds));

            DescribeStacksResponse response;
            try
            {
                response = await client.DescribeStacksAsync(
                    new DescribeStacksRequest { StackName = stackName });
            }
            catch (AmazonCloudFormationException ex) when (
                successStatus == "DELETE_COMPLETE" && ex.ErrorCode == "ValidationError")
            {
                _logger.LogInformation("Stack {StackName} deleted successfully.", stackName);
                return;
            }

            if (response.Stacks.Count == 0) return;

            var stack = response.Stacks[0];
            _logger.LogInformation("Stack status: {Status}", stack.StackStatus.Value);

            if (stack.StackStatus == successStatus)
                return;

            if (stack.StackStatus.Value.EndsWith("_FAILED") ||
                stack.StackStatus.Value.EndsWith("_ROLLBACK_COMPLETE"))
            {
                throw new System.InvalidOperationException(
                    $"Stack operation failed with status: {stack.StackStatus.Value}. " +
                    $"Reason: {stack.StackStatusReason}");
            }
        }

        throw new TimeoutException($"Timed out waiting for stack {stackName} to reach {successStatus}.");
    }

    private static async Task<Dictionary<string, string>> GetStackOutputsAsync(
        AmazonCloudFormationClient client, string stackName)
    {
        var response = await client.DescribeStacksAsync(
            new DescribeStacksRequest { StackName = stackName });

        return response.Stacks
            .FirstOrDefault()
            ?.Outputs
            .ToDictionary(o => o.OutputKey, o => o.OutputValue)
            ?? new Dictionary<string, string>();
    }
}
