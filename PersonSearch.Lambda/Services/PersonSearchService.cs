using Microsoft.Extensions.Logging;
using Npgsql;
using PersonSearch.Lambda.Models;

namespace PersonSearch.Lambda.Services;

/// <summary>
/// Queries the Aurora DSQL <c>persons</c> table for records whose first name and last
/// name are similar (case-insensitive, partial-match) to the supplied search terms.
/// </summary>
public class PersonSearchService
{
    private readonly IDsqlConnectionFactory _connectionFactory;
    private readonly ILogger<PersonSearchService> _logger;

    public PersonSearchService(IDsqlConnectionFactory connectionFactory, ILogger<PersonSearchService> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger;
    }

    /// <summary>
    /// Returns all <see cref="Person"/> records whose <c>first_name</c> contains
    /// <paramref name="firstName"/> and whose <c>last_name</c> contains
    /// <paramref name="lastName"/> (both comparisons are case-insensitive).
    /// </summary>
    /// <param name="firstName">First-name search term (may be partial).</param>
    /// <param name="lastName">Last-name search term (may be partial).</param>
    /// <param name="maxResults">Upper bound on the number of rows returned.</param>
    /// <returns>Matching person records, ordered by last name then first name.</returns>
    public virtual async Task<IReadOnlyList<Person>> SearchAsync(
        string firstName, string lastName, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
            throw new ArgumentException("At least one of firstName or lastName must be provided.");

        if (maxResults <= 0 || maxResults > 200)
            throw new ArgumentOutOfRangeException(nameof(maxResults),
                maxResults, "maxResults must be between 1 and 200 inclusive.");

        var firstNamePattern = $"%{firstName.Trim()}%";
        var lastNamePattern = $"%{lastName.Trim()}%";

        _logger.LogInformation(
            "Searching persons: firstName ILIKE '{FirstNamePattern}', lastName ILIKE '{LastNamePattern}', maxResults={Max}",
            firstNamePattern, lastNamePattern, maxResults);

        const string sql = """
            SELECT  person_id,
                    first_name,
                    last_name,
                    date_of_birth,
                    email,
                    phone_number,
                    created_at
            FROM    persons
            WHERE   first_name  ILIKE @firstNamePattern
              AND   last_name   ILIKE @lastNamePattern
            ORDER BY last_name, first_name
            LIMIT @maxResults
            """;

        await using var connection = await _connectionFactory.CreateConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);

        cmd.Parameters.AddWithValue("firstNamePattern", firstNamePattern);
        cmd.Parameters.AddWithValue("lastNamePattern", lastNamePattern);
        cmd.Parameters.AddWithValue("maxResults", maxResults);

        await using var reader = await cmd.ExecuteReaderAsync();

        var results = new List<Person>();
        while (await reader.ReadAsync())
        {
            results.Add(MapPerson(reader));
        }

        _logger.LogInformation("Search returned {Count} record(s).", results.Count);
        return results;
    }

    // ── mapping ───────────────────────────────────────────────────────────

    private static Person MapPerson(NpgsqlDataReader reader)
    {
        DateOnly? dateOfBirth = null;
        if (!reader.IsDBNull(reader.GetOrdinal("date_of_birth")))
        {
            var raw = reader.GetDateTime(reader.GetOrdinal("date_of_birth"));
            dateOfBirth = DateOnly.FromDateTime(raw);
        }

        return new Person
        {
            PersonId = reader.GetGuid(reader.GetOrdinal("person_id")),
            FirstName = reader.GetString(reader.GetOrdinal("first_name")),
            LastName = reader.GetString(reader.GetOrdinal("last_name")),
            DateOfBirth = dateOfBirth,
            Email = reader.IsDBNull(reader.GetOrdinal("email"))
                ? null
                : reader.GetString(reader.GetOrdinal("email")),
            PhoneNumber = reader.IsDBNull(reader.GetOrdinal("phone_number"))
                ? null
                : reader.GetString(reader.GetOrdinal("phone_number")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
        };
    }
}
