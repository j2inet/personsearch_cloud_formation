using Amazon.AuroraDsql.Npgsql;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PersonSearch.Lambda.Services;

/// <summary>
/// Creates authenticated connections to an Aurora DSQL cluster using
/// <see cref="DsqlDataSource"/> from the <c>Amazon.AuroraDsql.Npgsql</c> package,
/// which handles IAM token generation automatically.
/// </summary>
public sealed class DsqlConnectionFactory : IDsqlConnectionFactory, IAsyncDisposable
{
    private readonly DsqlDataSource _dataSource;
    private readonly ILogger<DsqlConnectionFactory> _logger;

    private DsqlConnectionFactory(DsqlDataSource dataSource, ILogger<DsqlConnectionFactory> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new <see cref="DsqlConnectionFactory"/> backed by a <see cref="DsqlDataSource"/>
    /// configured for the given endpoint and AWS region.
    /// </summary>
    public static async Task<DsqlConnectionFactory> CreateAsync(
        string endpoint, string region, ILogger<DsqlConnectionFactory> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        logger.LogDebug("Creating DsqlDataSource for {Endpoint} in {Region}", endpoint, region);

        var config = new DsqlConfig
        {
            Host   = endpoint,
            Port   = 5432,
            Region = region,
            User   = "lambda_user"
        };

        var dataSource = await DsqlDataSource.CreateAsync(config);
        return new DsqlConnectionFactory(dataSource, logger);
    }

    /// <summary>
    /// Opens and returns a new <see cref="NpgsqlConnection"/>.
    /// The caller is responsible for disposing it.
    /// </summary>
    public async Task<NpgsqlConnection> CreateConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Opening DSQL connection");
        return await _dataSource.OpenConnectionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync();
}
