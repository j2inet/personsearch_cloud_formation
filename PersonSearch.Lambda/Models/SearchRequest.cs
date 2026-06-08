namespace PersonSearch.Lambda.Models;

/// <summary>
/// Input payload for the PersonSearch Lambda function.
/// </summary>
public sealed class SearchRequest
{
    /// <summary>
    /// First name to search for. Partial matches are included (case-insensitive).
    /// </summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// Last name to search for. Partial matches are included (case-insensitive).
    /// </summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of results to return. Defaults to 50.
    /// </summary>
    public int MaxResults { get; init; } = 50;
}
