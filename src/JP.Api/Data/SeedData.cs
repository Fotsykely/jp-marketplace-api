using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using JP.Api.Entities;

namespace JP.Api.Data;

/// <summary>Demo data so there is something to display from the very first launch.</summary>
public static class SeedData
{
    // Demo password shared by the seeded accounts (dev only)
    private const string DemoPassword = "Password123!";

    public static async Task SeedAsync(AppDbContext db, UserManager<User> userManager)
    {
        if (await db.Categories.AnyAsync()) return;

        var femmes = new Category { Name = "Femmes", Slug = "femmes" };
        var hommes = new Category { Name = "Hommes", Slug = "hommes" };
        var robes = new Category { Name = "Robes", Slug = "femmes-robes", Parent = femmes };
        var vestes = new Category { Name = "Vestes", Slug = "hommes-vestes", Parent = hommes };
        var baskets = new Category { Name = "Baskets", Slug = "hommes-baskets", Parent = hommes };
        db.Categories.AddRange(femmes, hommes, robes, vestes, baskets);

        var alice = new User { UserName = "alice", Email = "alice@jp-marketplace.dev", City = "Antananarivo" };
        var bob = new User { UserName = "bob", Email = "bob@jp-marketplace.dev", City = "Toamasina" };
        // CreateAsync hashes the password and saves the user right away
        await userManager.CreateAsync(alice, DemoPassword);
        await userManager.CreateAsync(bob, DemoPassword);

        db.Items.AddRange(
            new Item
            {
                Title = "Robe d'été fleurie", Description = "Portée deux fois, comme neuve.",
                Price = 45000, Brand = "Zara", Size = "M", Condition = ItemCondition.VeryGood,
                Category = robes, Seller = alice,
                Photos = [new ItemPhoto { Url = "https://picsum.photos/seed/robe/600/800", Position = 0 }]
            },
            new Item
            {
                Title = "Veste en jean", Description = "Coupe classique, légère usure aux manches.",
                Price = 60000, Brand = "Levi's", Size = "L", Condition = ItemCondition.Good,
                Category = vestes, Seller = bob,
                Photos = [new ItemPhoto { Url = "https://picsum.photos/seed/veste/600/800", Position = 0 }]
            },
            new Item
            {
                Title = "Air Max 90", Description = "Neuves dans la boîte, mauvaise taille.",
                Price = 250000, Brand = "Nike", Size = "43", Condition = ItemCondition.NewWithTags,
                Category = baskets, Seller = bob
            });

        await db.SaveChangesAsync();
    }
}
