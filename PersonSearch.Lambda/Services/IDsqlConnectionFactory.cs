using Npgsql;

namespace PersonSearch.Lambda.Services;

/// <summary>
/// Abstraction over Aurora DSQL connection creation, enabling unit-testing without
/// a live database cluster.
/// </summary>
public interface IDsqlConnectionFactory
{
    /// <summary>
    /// Opens and returns a new authenticated <see cref="NpgsqlConnection"/>.
    /// The caller is responsible for disposing it.
    /// </summary>
    Task<NpgsqlConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
