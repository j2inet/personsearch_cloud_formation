namespace PersonSearch.Lambda.Models;

/// <summary>
/// Represents a person record stored in the Aurora DSQL <c>persons</c> table.
/// </summary>
public sealed class Person
{
    /// <summary>Unique identifier for the person record.</summary>
    public Guid PersonId { get; init; }

    /// <summary>Person's first (given) name.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Person's last (family) name.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Date of birth (optional).</summary>
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>Email address (optional).</summary>
    public string? Email { get; init; }

    /// <summary>Phone number (optional).</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>UTC timestamp when the record was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
