using Microsoft.EntityFrameworkCore;
using JP.Api.Entities;

namespace JP.Api.Data;

// Spring equivalent: the EntityManager and JPA repositories combined.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemPhoto> ItemPhotos => Set<ItemPhoto>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.UserName).IsUnique();
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
    }
}
