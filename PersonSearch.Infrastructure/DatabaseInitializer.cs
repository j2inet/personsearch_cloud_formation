using Amazon;
using Amazon.DSQL.Util;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PersonSearch.Infrastructure;

/// <summary>
/// Creates the <c>persons</c> table in the Aurora DSQL cluster after deployment.
/// Uses <see cref="DSQLAuthTokenGenerator"/> to obtain an admin IAM auth token,
/// then connects via Npgsql to run the DDL.
/// </summary>
public class DatabaseInitializer
{
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(ILogger<DatabaseInitializer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connects to the Aurora DSQL cluster and creates the <c>persons</c> table
    /// if it does not already exist.
    /// </summary>
    /// <param name="dsqlEndpoint">Hostname of the DSQL cluster (from CloudFormation output).</param>
    /// <param name="region">AWS region where the cluster resides.</param>
    public async Task InitializeSchemaAsync(string dsqlEndpoint, string region)
    {
        _logger.LogInformation("Connecting to DSQL endpoint {Endpoint}...", dsqlEndpoint);

        var regionEndpoint = RegionEndpoint.GetBySystemName(region);
        var authToken = await DSQLAuthTokenGenerator
            .GenerateDbConnectAdminAuthTokenAsync(regionEndpoint, dsqlEndpoint);

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = dsqlEndpoint,
            Port = 5432,
            Database = "postgres",
            Username = "admin",
            Password = authToken,
            SslMode = SslMode.Require
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        _logger.LogInformation("Connected. Creating schema...");

        const string createTableSql = """
            CREATE TABLE IF NOT EXISTS persons (
                person_id    UUID         NOT NULL DEFAULT gen_random_uuid(),
                first_name   VARCHAR(100) NOT NULL,
                last_name    VARCHAR(100) NOT NULL,
                date_of_birth DATE,
                email        VARCHAR(255),
                phone_number VARCHAR(50),
                created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
                PRIMARY KEY (person_id)
            );

            CREATE INDEX IF NOT EXISTS idx_persons_first_name ON persons (first_name);
            CREATE INDEX IF NOT EXISTS idx_persons_last_name  ON persons (last_name);
            """;

        await using var cmd = new NpgsqlCommand(createTableSql, connection);
        await cmd.ExecuteNonQueryAsync();

        _logger.LogInformation("Schema created successfully.");
    }
}
