namespace JP.Api.Entities;

/// <summary>
/// One attempt to pay for a reserved item through a Stripe Checkout Session.
/// The amount is copied from the accepted offer at creation time, so it never changes afterwards.
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Amount charged, in major units (e.g. 45.00 EUR). Converted to minor units for Stripe.</summary>
    public decimal Amount { get; set; }

    /// <summary>Lower-case ISO 4217 code used for this payment (e.g. "eur").</summary>
    public string Currency { get; set; } = "";

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When Stripe confirmed the money. Null until then.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Checkout Session id (cs_...). Unique: lets the webhook find the payment.</summary>
    public string StripeSessionId { get; set; } = "";

    /// <summary>PaymentIntent id (pi_...), known once the session is completed. Needed for refunds.</summary>
    public string? StripePaymentIntentId { get; set; }

    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;

    public Guid BuyerId { get; set; }
    public User Buyer { get; set; } = null!;
}

/// <summary>
/// A Stripe webhook event we already handled. Stripe can deliver the same event several times
/// (retries, replays), so we record each event id and ignore the ones we have seen.
/// </summary>
public class StripeWebhookEvent
{
    /// <summary>The Stripe event id (evt_...). It is the primary key: the database enforces "once only".</summary>
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
