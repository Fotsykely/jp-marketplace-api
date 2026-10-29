namespace JP.Api.Entities;

/// <summary>
/// Member of the platform (seller and/or buyer).
/// Roadmap step 2: replace with ASP.NET Core Identity.
/// </summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Item> Items { get; set; } = [];
}
