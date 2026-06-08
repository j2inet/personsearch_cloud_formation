using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using PersonSearch.Lambda.Models;
using PersonSearch.Lambda.Services;

// Register the Lambda serializer for the assembly
[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace PersonSearch.Lambda;

/// <summary>
/// AWS Lambda entry point for the PersonSearch function.
///
/// <para>
/// The function accepts a <see cref="SearchRequest"/> JSON payload, queries the Aurora
/// DSQL <c>persons</c> table for records with matching (partial, case-insensitive)
/// first and last names, and returns a <see cref="SearchResponse"/> containing all
/// matching <see cref="Person"/> records.
/// </para>
///
/// <para>Required environment variables:</para>
/// <list type="bullet">
///   <item><c>DSQL_ENDPOINT</c> – hostname of the Aurora DSQL cluster</item>
///   <item><c>AWS_REGION</c> – AWS region (injected automatically by the Lambda runtime)</item>
/// </list>
/// </summary>
public sealed class Function
{
    private readonly PersonSearchService _searchService;
    private readonly ILogger<Function> _logger;

    /// <summary>
    /// Parameterless constructor used by the Lambda runtime.
    /// Configuration is read from environment variables.
    /// Initialization is asynchronous; the <see cref="DsqlConnectionFactory"/> is
    /// created eagerly during the cold-start phase.
    /// </summary>
    public Function() : this(BuildSearchService().GetAwaiter().GetResult(),
        BuildLogger()) { }

    /// <summary>Constructor for dependency injection and unit tests.</summary>
    public Function(PersonSearchService searchService, ILogger<Function> logger)
    {
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));
        _logger = logger;
    }

    /// <summary>
    /// Lambda handler: search for persons by similar first and last name.
    /// </summary>
    /// <param name="request">Search parameters (first name, last name, optional max results).</param>
    /// <param name="context">Lambda execution context.</param>
    /// <returns>A <see cref="SearchResponse"/> with matching person records.</returns>
    public async Task<SearchResponse> FunctionHandler(SearchRequest request, ILambdaContext context)
    {
        _logger.LogInformation(
            "Request received: firstName='{FirstName}', lastName='{LastName}', maxResults={Max}",
            request.FirstName, request.LastName, request.MaxResults);

        if (string.IsNullOrWhiteSpace(request.FirstName) && string.IsNullOrWhiteSpace(request.LastName))
        {
            _logger.LogWarning("Both firstName and lastName are empty – returning empty result set.");
            return new SearchResponse
            {
                Results = Array.Empty<Person>(),
                FirstNamePattern = string.Empty,
                LastNamePattern = string.Empty
            };
        }

        var results = await _searchService.SearchAsync(
            request.FirstName ?? string.Empty,
            request.LastName ?? string.Empty,
            request.MaxResults);

        return new SearchResponse
        {
            Results = results,
            FirstNamePattern = $"%{request.FirstName?.Trim()}%",
            LastNamePattern = $"%{request.LastName?.Trim()}%"
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static ILoggerFactory BuildLoggerFactory() =>
        LoggerFactory.Create(builder =>
        {
            builder.AddConsole(options => options.FormatterName = ConsoleFormatterNames.Simple);
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

    private static ILogger<Function> BuildLogger() =>
        BuildLoggerFactory().CreateLogger<Function>();

    private static async Task<PersonSearchService> BuildSearchService()
    {
        var loggerFactory = BuildLoggerFactory();

        var endpoint = Environment.GetEnvironmentVariable("DSQL_ENDPOINT")
            ?? throw new InvalidOperationException("DSQL_ENDPOINT environment variable is not set.");

        var region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? "us-east-1";

        var connectionFactory = await DsqlConnectionFactory.CreateAsync(
            endpoint,
            region,
            loggerFactory.CreateLogger<DsqlConnectionFactory>());

        return new PersonSearchService(
            connectionFactory,
            loggerFactory.CreateLogger<PersonSearchService>());
    }
}
