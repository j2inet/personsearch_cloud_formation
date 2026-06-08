namespace PersonSearch.Lambda.Models;

/// <summary>
/// Output payload returned by the PersonSearch Lambda function.
/// </summary>
public sealed class SearchResponse
{
    /// <summary>Person records that matched the search criteria.</summary>
    public IReadOnlyList<Person> Results { get; init; } = Array.Empty<Person>();

    /// <summary>Total number of matching records returned.</summary>
    public int Count => Results.Count;

    /// <summary>First-name pattern used in the query (for diagnostics).</summary>
    public string FirstNamePattern { get; init; } = string.Empty;

    /// <summary>Last-name pattern used in the query (for diagnostics).</summary>
    public string LastNamePattern { get; init; } = string.Empty;
}
