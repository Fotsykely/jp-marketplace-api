using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using JP.Api.Entities;

namespace JP.Api.Data;

// Spring equivalent: the EntityManager and JPA repositories combined.
// IdentityDbContext adds the Identity tables (users, roles...) and the Users DbSet.
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<User, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemPhoto> ItemPhotos => Set<ItemPhoto>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Offer> Offers => Set<Offer>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Required: Identity configures its own tables in the base method
        base.OnModelCreating(b);

        // RequireUniqueEmail (Program.cs) only checks in code, which is racy under concurrent
        // sign-ups. Identity's default "EmailIndex" is NOT unique, so we make it unique here.
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
            e.Property(u => u.UserName).HasMaxLength(50);
            e.Property(u => u.Email).HasMaxLength(255);
        });

        b.Entity<Category>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasOne(c => c.Parent)
             .WithMany(c => c.Children)
             .HasForeignKey(c => c.ParentId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Item>(e =>
        {
            e.Property(i => i.Title).HasMaxLength(100);
            e.Property(i => i.Description).HasMaxLength(2000);
            e.Property(i => i.Price).HasPrecision(10, 2);
            // Enums stored as text: more readable in the database than an integer
            e.Property(i => i.Condition).HasConversion<string>().HasMaxLength(30);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(i => new { i.Status, i.CreatedAt });

            e.HasOne(i => i.Seller).WithMany(u => u.Items)
             .HasForeignKey(i => i.SellerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Category).WithMany()
             .HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(i => i.Photos).WithOne(p => p.Item)
             .HasForeignKey(p => p.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Favorite>(e =>
        {
            // Composite key: the database itself prevents favouriting the same item twice
            e.HasKey(f => new { f.UserId, f.ItemId });
            e.HasOne(f => f.User).WithMany()
             .HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Item).WithMany()
             .HasForeignKey(f => f.ItemId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Offer>(e =>
        {
            e.Property(o => o.Amount).HasPrecision(10, 2);
            e.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(o => new { o.ItemId, o.Status });
            // At most one pending offer per buyer and item, enforced by the database itself
            e.HasIndex(o => new { o.ItemId, o.BuyerId }).IsUnique()
             .HasFilter("\"Status\" = 'Pending'");

            e.HasOne(o => o.Item).WithMany()
             .HasForeignKey(o => o.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Buyer).WithMany()
             .HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
