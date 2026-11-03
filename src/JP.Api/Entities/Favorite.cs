namespace JP.Api.Entities;

/// <summary>An item a user saved to find it again later. One row per (user, item) pair.</summary>
public class Favorite
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
