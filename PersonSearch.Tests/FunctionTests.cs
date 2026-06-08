using Amazon.Lambda.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonSearch.Lambda;
using PersonSearch.Lambda.Models;
using PersonSearch.Lambda.Services;
using Xunit;

namespace PersonSearch.Tests;

public class FunctionTests
{
    private readonly Mock<ILambdaContext> _mockContext;

    public FunctionTests()
    {
        _mockContext = new Mock<ILambdaContext>();
        _mockContext.Setup(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
    }

    /// <summary>
    /// Builds a <see cref="Function"/> whose <see cref="PersonSearchService"/> is backed by
    /// a supplied delegate instead of a real Aurora DSQL cluster.
    /// </summary>
    private static Function BuildFunction(
        Func<string, string, int, Task<IReadOnlyList<Person>>> searchImpl)
    {
        var fakeFactory = new FakeConnectionFactory();
        var service = new TestablePersonSearchService(fakeFactory, searchImpl);
        return new Function(service, NullLogger<Function>.Instance);
    }

    [Fact]
    public async Task FunctionHandler_ReturnsPeople_WhenMatchesFound()
    {
        var expectedPeople = new List<Person>
        {
            new() { PersonId = Guid.NewGuid(), FirstName = "John",   LastName = "Smith",    CreatedAt = DateTimeOffset.UtcNow },
            new() { PersonId = Guid.NewGuid(), FirstName = "Johnny", LastName = "Smithson", CreatedAt = DateTimeOffset.UtcNow }
        };

        var function = BuildFunction((_, _, _) =>
            Task.FromResult<IReadOnlyList<Person>>(expectedPeople));

        var response = await function.FunctionHandler(
            new SearchRequest { FirstName = "John", LastName = "Smith", MaxResults = 50 },
            _mockContext.Object);

        Assert.NotNull(response);
        Assert.Equal(2, response.Count);
        Assert.Equal("%John%", response.FirstNamePattern);
        Assert.Equal("%Smith%", response.LastNamePattern);
    }

    [Fact]
    public async Task FunctionHandler_ReturnsEmptyList_WhenNoMatchesFound()
    {
        var function = BuildFunction((_, _, _) =>
            Task.FromResult<IReadOnlyList<Person>>(new List<Person>()));

        var response = await function.FunctionHandler(
            new SearchRequest { FirstName = "Xyz", LastName = "Zzz" },
            _mockContext.Object);

        Assert.NotNull(response);
        Assert.Empty(response.Results);
        Assert.Equal(0, response.Count);
    }

    [Fact]
    public async Task FunctionHandler_ReturnsEmpty_WhenBothNamesAreEmpty()
    {
        bool searchCalled = false;
        var function = BuildFunction((_, _, _) =>
        {
            searchCalled = true;
            return Task.FromResult<IReadOnlyList<Person>>(new List<Person>());
        });

        var response = await function.FunctionHandler(
            new SearchRequest { FirstName = "", LastName = "" },
            _mockContext.Object);

        Assert.NotNull(response);
        Assert.Empty(response.Results);
        Assert.False(searchCalled, "SearchAsync should not be called when both names are empty.");
    }

    [Fact]
    public async Task FunctionHandler_SearchesWithFirstNameOnly_WhenLastNameEmpty()
    {
        var alice = new Person
        {
            PersonId = Guid.NewGuid(), FirstName = "Alice",
            LastName = "Brown", CreatedAt = DateTimeOffset.UtcNow
        };

        var function = BuildFunction((fn, ln, _) =>
        {
            Assert.Equal("Alice", fn);
            Assert.Equal("", ln);
            return Task.FromResult<IReadOnlyList<Person>>(new List<Person> { alice });
        });

        var response = await function.FunctionHandler(
            new SearchRequest { FirstName = "Alice", LastName = "", MaxResults = 50 },
            _mockContext.Object);

        Assert.Single(response.Results);
        Assert.Equal("Alice", response.Results[0].FirstName);
    }

    // ── test doubles ──────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="IDsqlConnectionFactory"/> stub – never called in these tests
    /// because <see cref="TestablePersonSearchService"/> overrides <c>SearchAsync</c>
    /// before any connection is requested.
    /// </summary>
    private sealed class FakeConnectionFactory : IDsqlConnectionFactory
    {
        public Task<Npgsql.NpgsqlConnection> CreateConnectionAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No real connection should be opened in unit tests.");
    }

    /// <summary>
    /// Overrides <see cref="PersonSearchService.SearchAsync"/> with a caller-supplied
    /// delegate so tests control results without touching a database.
    /// </summary>
    private sealed class TestablePersonSearchService : PersonSearchService
    {
        private readonly Func<string, string, int, Task<IReadOnlyList<Person>>> _impl;

        public TestablePersonSearchService(
            IDsqlConnectionFactory factory,
            Func<string, string, int, Task<IReadOnlyList<Person>>> impl)
            : base(factory, NullLogger<PersonSearchService>.Instance)
        {
            _impl = impl;
        }

        public override Task<IReadOnlyList<Person>> SearchAsync(
            string firstName, string lastName, int maxResults = 50) =>
            _impl(firstName, lastName, maxResults);
    }
}
