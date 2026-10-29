namespace JP.Api.Entities;

/// <summary>A listing (an item put up for sale).</summary>
public class Item
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }
    public string? Brand { get; set; }
    public string? Size { get; set; }
    public ItemCondition Condition { get; set; }
    public ItemStatus Status { get; set; } = ItemStatus.Available;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public Guid SellerId { get; set; }
    public User Seller { get; set; } = null!;

    public List<ItemPhoto> Photos { get; set; } = [];
}

/// <summary>Photo of a listing. Step 3: real upload (local storage, then S3/Azure Blob).</summary>
public class ItemPhoto
{
    public int Id { get; set; }
    public required string Url { get; set; }
    public int Position { get; set; }

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;
}
