using Microsoft.AspNetCore.Identity;

namespace JP.Api.Entities;

/// <summary>
/// Member of the platform (seller and/or buyer).
/// Id, UserName, Email, PasswordHash... are inherited from IdentityUser.
/// The Guid type parameter keeps Guid identifiers, so Item.SellerId is unchanged.
/// </summary>
public class User : IdentityUser<Guid>
{
    public string? City { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Item> Items { get; set; } = [];
}
