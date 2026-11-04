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

    // The state machine: the only legal status changes. Everything else is refused.
    //   Available -> Reserved   (offer accepted / purchase started)
    //   Reserved  -> Sold       (payment confirmed)
    //   Reserved  -> Available  (reservation cancelled, payment failed or expired)
    // Notably NOT allowed: Available -> Sold (no reservation = no lock) and anything out of Sold.
    private static readonly Dictionary<ItemStatus, ItemStatus[]> AllowedTransitions = new()
    {
        [ItemStatus.Available] = [ItemStatus.Reserved],
        [ItemStatus.Reserved] = [ItemStatus.Sold, ItemStatus.Available],
        [ItemStatus.Sold] = []
    };

    public bool CanTransitionTo(ItemStatus next) => AllowedTransitions[Status].Contains(next);

    /// <summary>Changes the status if the transition is legal; returns false (and changes nothing) otherwise.</summary>
    public bool TryTransitionTo(ItemStatus next)
    {
        if (!CanTransitionTo(next)) return false;
        Status = next;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }
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
