namespace JP.Api.Entities;

/// <summary>A price offer a buyer makes on a listing, below the asking price.</summary>
public class Offer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal Amount { get; set; }
    public OfferStatus Status { get; set; } = OfferStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the seller accepted or declined. Null while pending.</summary>
    public DateTime? RespondedAt { get; set; }

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public Guid BuyerId { get; set; }
    public User Buyer { get; set; } = null!;
}
