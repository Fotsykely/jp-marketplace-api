using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Stripe;
using Stripe.Checkout;
using JP.Api.Data;
using JP.Api.Dtos;
using JP.Api.Entities;

namespace JP.Api.Services;

/// <summary>Why a checkout was refused. The controller turns each one into an HTTP status.</summary>
public enum PaymentError
{
    NotFound,
    NotBuyer,         // only the buyer of the accepted offer may pay
    ItemUnavailable,  // the item is not reserved (anymore)
    AlreadyPaid       // Stripe already took the money, the confirmation is being processed
}

/// <summary>Either a value or an error, same idea as <see cref="OfferOutcome{T}"/>.</summary>
public readonly record struct PaymentOutcome<T>(T? Value, PaymentError? Error)
{
    public static implicit operator PaymentOutcome<T>(T value) => new(value, null);
    public static implicit operator PaymentOutcome<T>(PaymentError error) => new(default, error);
}

public interface IPaymentService
{
    /// <summary>Creates (or reuses) a Stripe Checkout Session for the reserved item. Buyer only.</summary>
    Task<PaymentOutcome<CheckoutResponse>> CreateCheckoutAsync(Guid itemId, Guid buyerId, CancellationToken ct);

    /// <summary>
    /// Handles one webhook call from Stripe. Returns false when the signature is invalid
    /// (the call does not come from Stripe), true otherwise, including for events seen before.
    /// </summary>
    Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken ct);
}

public class PaymentService(
    AppDbContext db,
    IOptions<StripeOptions> options,
    ILogger<PaymentService> logger) : IPaymentService
{
    // Stripe wants integers in the smallest unit of the currency (cents for EUR).
    // These currencies have no minor unit: 1 MGA is sent as 1, not 100.
    private static readonly HashSet<string> ZeroDecimalCurrencies =
    [
        "bif", "clp", "djf", "gnf", "jpy", "kmf", "krw", "mga",
        "pyg", "rwf", "ugx", "vnd", "vuv", "xaf", "xof", "xpf"
    ];

    private readonly StripeOptions stripe = options.Value;

    /// <summary>Major units (45.00) to Stripe's integer minor units (4500).</summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var factor = ZeroDecimalCurrencies.Contains(currency.ToLowerInvariant()) ? 1m : 100m;
        return (long)Math.Round(amount * factor, MidpointRounding.AwayFromZero);
    }

    // ---------------------------------------------------------------- checkout

    public async Task<PaymentOutcome<CheckoutResponse>> CreateCheckoutAsync(
        Guid itemId, Guid buyerId, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => new { i.Title, i.Status })
            .FirstOrDefaultAsync(ct);
        if (item is null) return PaymentError.NotFound;
        if (item.Status != ItemStatus.Reserved) return PaymentError.ItemUnavailable;

        // The accepted offer decides who pays and how much (not the asking price)
        var offer = await db.Offers.AsNoTracking()
            .FirstOrDefaultAsync(o => o.ItemId == itemId && o.Status == OfferStatus.Accepted, ct);
        if (offer is null) return PaymentError.ItemUnavailable;
        if (offer.BuyerId != buyerId) return PaymentError.NotBuyer;

        var sessions = new SessionService();

        // Reuse the open payment page instead of creating a second one (double click, page reload)
        var pending = await db.Payments
            .FirstOrDefaultAsync(p => p.OfferId == offer.Id && p.Status == PaymentStatus.Pending, ct);
        if (pending is not null)
        {
            var existing = await sessions.GetAsync(pending.StripeSessionId, cancellationToken: ct);
            if (existing.Status == "open" && existing.Url is not null)
                return new CheckoutResponse(existing.Url);
            if (existing.Status == "complete")
                return PaymentError.AlreadyPaid; // the webhook will confirm shortly

            // Session expired but the webhook has not told us yet: close it ourselves, then start over
            pending.Status = PaymentStatus.Expired;
            await db.SaveChangesAsync(ct);
        }

        var currency = stripe.Currency.ToLowerInvariant();
        var payment = new Payment
        {
            ItemId = itemId,
            OfferId = offer.Id,
            BuyerId = buyerId,
            Amount = offer.Amount,
            Currency = currency
        };

        // The idempotency key makes two simultaneous requests for the same attempt receive the
        // same Stripe session instead of two different ones.
        var attempt = await db.Payments.CountAsync(p => p.OfferId == offer.Id, ct);
        var session = await sessions.CreateAsync(
            new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = stripe.SuccessUrl,
                CancelUrl = stripe.CancelUrl,
                ClientReferenceId = payment.Id.ToString(),
                // Metadata comes back untouched in webhook events
                Metadata = new Dictionary<string, string>
                {
                    ["paymentId"] = payment.Id.ToString(),
                    ["itemId"] = itemId.ToString(),
                    ["offerId"] = offer.Id.ToString()
                },
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency,
                            UnitAmount = ToMinorUnits(offer.Amount, currency),
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = item.Title }
                        }
                    }
                ]
            },
            new RequestOptions { IdempotencyKey = $"checkout-{offer.Id}-{attempt}" },
            ct);

        payment.StripeSessionId = session.Id;
        db.Payments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A parallel request already stored a payment for this very session: same page for everyone
            logger.LogInformation("Checkout session {SessionId} was already stored", session.Id);
        }

        return new CheckoutResponse(session.Url);
    }

    // ----------------------------------------------------------------- webhook

    public async Task<bool> HandleWebhookAsync(string payload, string? signature, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stripe.WebhookSecret))
            throw new InvalidOperationException("Missing 'Stripe:WebhookSecret' configuration.");
        if (string.IsNullOrEmpty(signature)) return false;

        Event stripeEvent;
        try
        {
            // Checks the Stripe-Signature header: proves the call really comes from Stripe
            stripeEvent = EventUtility.ConstructEvent(
                payload, signature, stripe.WebhookSecret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Rejected a webhook call with an invalid signature");
            return false;
        }

        // One transaction: "event recorded" and "event handled" succeed or fail together.
        // If handling fails, the event is not recorded and Stripe's retry will do it again.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.StripeWebhookEvents.Add(new StripeWebhookEvent { Id = stripeEvent.Id, Type = stripeEvent.Type });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Primary key already there: we handled this event before. Answer 200 so Stripe stops retrying.
            logger.LogInformation("Webhook event {EventId} already processed, ignoring", stripeEvent.Id);
            return true;
        }

        if (stripeEvent.Data.Object is Session session)
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed" when session.PaymentStatus == "paid":
                    await OnPaidAsync(session, ct);
                    break;
                case "checkout.session.expired":
                    await OnExpiredAsync(session, ct);
                    break;
            }
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private async Task OnPaidAsync(Session session, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.StripeSessionId == session.Id, ct);
        if (payment is null)
        {
            logger.LogWarning("Paid session {SessionId} matches no payment", session.Id);
            return;
        }
        if (payment.Status != PaymentStatus.Pending) return;

        var now = DateTime.UtcNow;
        payment.StripePaymentIntentId = session.PaymentIntentId;
        payment.PaidAt = now;

        // Atomic Reserved -> Sold. The guard says: still reserved AND still reserved for THIS offer
        // (the item may have been released, then reserved again by another buyer).
        var itemId = payment.ItemId;
        var offerId = payment.OfferId;
        var sold = await db.Items
            .Where(i => i.Id == itemId
                        && i.Status == ItemStatus.Reserved
                        && db.Offers.Any(o => o.Id == offerId && o.Status == OfferStatus.Accepted))
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, ItemStatus.Sold)
                .SetProperty(i => i.UpdatedAt, now), ct);

        if (sold == 1)
        {
            payment.Status = PaymentStatus.Succeeded;
        }
        else
        {
            // Paid too late: the reservation was released. Give the money back automatically.
            logger.LogWarning("Payment {PaymentId} arrived after the reservation ended: refunding", payment.Id);
            await new RefundService().CreateAsync(
                new RefundCreateOptions { PaymentIntent = session.PaymentIntentId },
                new RequestOptions { IdempotencyKey = $"refund-{payment.Id}" },
                ct);
            payment.Status = PaymentStatus.Refunded;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task OnExpiredAsync(Session session, CancellationToken ct)
    {
        // The item stays Reserved: the seller can cancel, or the reservation expires by itself
        await db.Payments
            .Where(p => p.StripeSessionId == session.Id && p.Status == PaymentStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Expired), ct);
    }

    // 23505 = unique_violation in PostgreSQL
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
