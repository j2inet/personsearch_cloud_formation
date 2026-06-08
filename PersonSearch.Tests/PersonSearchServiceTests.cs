using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PersonSearch.Lambda.Services;
using Xunit;

namespace PersonSearch.Tests;

public class PersonSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ThrowsArgumentException_WhenBothNamesEmpty()
    {
        var service = new PersonSearchService(
            new NeverCalledConnectionFactory(),
            NullLogger<PersonSearchService>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SearchAsync("", ""));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public async Task SearchAsync_ThrowsArgumentOutOfRangeException_WhenMaxResultsOutOfRange(int maxResults)
    {
        var service = new PersonSearchService(
            new NeverCalledConnectionFactory(),
            NullLogger<PersonSearchService>.Instance);

        // At least one name is provided so that only maxResults validation fires
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SearchAsync("John", "Smith", maxResults));
    }

    /// <summary>
    /// Stub that will throw if <c>CreateConnectionAsync</c> is ever invoked,
    /// proving the validation path short-circuits before touching the database.
    /// </summary>
    private sealed class NeverCalledConnectionFactory : IDsqlConnectionFactory
    {
        public Task<NpgsqlConnection> CreateConnectionAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "CreateConnectionAsync must not be called during input-validation tests.");
    }
}
