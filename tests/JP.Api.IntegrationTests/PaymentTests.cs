using System.Net;
using System.Security.Cryptography;
using System.Text;
using JP.Api.Dtos;
using JP.Api.Entities;
using JP.Api.IntegrationTests.Infrastructure;
using JP.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JP.Api.IntegrationTests;

/// <summary>
/// Checkout refusals, the Stripe webhook and reservation expiry.
/// The checkout happy path is not tested here: it calls Stripe's real servers.
/// The webhook, however, is tested for real: we sign payloads exactly like Stripe does.
/// </summary>
public class PaymentTests(IntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private const string SessionId = "cs_test_session_1";
    private const string PaymentIntentId = "pi_test_1";

    // ---------- Helpers ----------

    /// <summary>A reserved item plus a pending payment row, as if the buyer had opened the Stripe page.</summary>
    private async Task<(ItemDetailDto Item, Guid PaymentId)> ArrangePendingPaymentAsync(
        TestUser seller, TestUser buyer, string sessionId = SessionId)
    {
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        var paymentId = await WithDbAsync(async db =>
        {
            var payment = new Payment
            {
                ItemId = item.Id, OfferId = offer.Id, BuyerId = buyer.Id,
                Amount = offer.Amount, Currency = "eur", StripeSessionId = sessionId
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            return payment.Id;
        });
        return (item, paymentId);
    }

    private static string EventJson(string eventId, string type, string sessionId, string paymentStatus) => $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "{{type}}",
          "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "payment_status": "{{paymentStatus}}",
              "payment_intent": "{{PaymentIntentId}}"
            }
          }
        }
        """;

    /// <summary>Stripe's scheme: header "t=timestamp,v1=HMAC-SHA256(secret, "timestamp.payload")".</summary>
    private static string Sign(string payload, string secret = JpWebApplicationFactory.StripeWebhookSecret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (signature is not null) request.Headers.Add("Stripe-Signature", signature);
        return CreateClient().SendAsync(request);
    }

    private Task<Payment> GetPaymentAsync(Guid paymentId) =>
        WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId));

    private Task<int> CountWebhookEventsAsync() =>
        WithDbAsync(db => db.StripeWebhookEvents.CountAsync());

    // ---------- Checkout refusals (all happen before any call to Stripe) ----------

    [Fact]
    public async Task Checkout_WithoutToken_Returns401()
    {
        var response = await CreateClient().PostAsync($"/api/items/{Guid.NewGuid()}/checkout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_OfUnknownItem_Returns404()
    {
        var buyer = await RegisterUserAsync();

        var response = await buyer.Client.PostAsync($"/api/items/{Guid.NewGuid()}/checkout", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_OfAnItemThatIsNotReserved_Returns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await buyer.Client.PostAsync($"/api/items/{item.Id}/checkout", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_OfAReservedItemWithoutAcceptedOffer_Returns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        await SetItemStatusAsync(item.Id, ItemStatus.Reserved);

        var response = await buyer.Client.PostAsync($"/api/items/{item.Id}/checkout", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_ByAnotherUserThanTheBuyerOfTheAcceptedOffer_Returns403()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var stranger = await RegisterUserAsync("mallory");
        var (item, _) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());

        var byStranger = await stranger.Client.PostAsync($"/api/items/{item.Id}/checkout", null);
        var bySeller = await seller.Client.PostAsync($"/api/items/{item.Id}/checkout", null);

        Assert.Equal(HttpStatusCode.Forbidden, byStranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bySeller.StatusCode);
    }

    // ---------- Webhook: signature ----------

    [Fact]
    public async Task Webhook_WithoutSignature_Returns400()
    {
        var payload = EventJson("evt_1", "checkout.session.completed", SessionId, "paid");

        var response = await PostWebhookAsync(payload, signature: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountWebhookEventsAsync());
    }

    [Fact]
    public async Task Webhook_WithAGarbageSignature_Returns400()
    {
        var payload = EventJson("evt_1", "checkout.session.completed", SessionId, "paid");

        var response = await PostWebhookAsync(payload, "t=123,v1=deadbeef");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_SignedWithTheWrongSecret_Returns400_AndTheItemStaysReserved()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, _) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_forged", "checkout.session.completed", SessionId, "paid");

        var response = await PostWebhookAsync(payload, Sign(payload, secret: "whsec_attacker_guess"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task Webhook_WhenThePayloadIsAlteredAfterSigning_Returns400()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, _) = await ArrangePendingPaymentAsync(seller, buyer);
        var signedFor = EventJson("evt_1", "checkout.session.completed", SessionId, "unpaid");
        var signature = Sign(signedFor);
        var tampered = signedFor.Replace("unpaid", "paid");

        var response = await PostWebhookAsync(tampered, signature);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
    }

    // ---------- Webhook: events ----------

    [Fact]
    public async Task Webhook_PaymentCompleted_MarksTheItemSold_AndThePaymentSucceeded()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, paymentId) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_paid", "checkout.session.completed", SessionId, "paid");

        // Anonymous client: Stripe has no JWT, the signature is the proof of identity
        var response = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ItemStatus.Sold, await GetItemStatusAsync(item.Id));
        var payment = await GetPaymentAsync(paymentId);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(PaymentIntentId, payment.StripePaymentIntentId);
        Assert.NotNull(payment.PaidAt);
    }

    [Fact]
    public async Task Webhook_DeliveredTwice_IsHandledOnce_AndBothCallsReturn200()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, paymentId) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_replayed", "checkout.session.completed", SessionId, "paid");

        var first = await PostWebhookAsync(payload, Sign(payload));
        var paidAtAfterFirst = (await GetPaymentAsync(paymentId)).PaidAt;
        var second = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // 200 so Stripe stops retrying
        Assert.Equal(1, await CountWebhookEventsAsync());
        Assert.Equal(ItemStatus.Sold, await GetItemStatusAsync(item.Id));
        Assert.Equal(paidAtAfterFirst, (await GetPaymentAsync(paymentId)).PaidAt);
    }

    [Fact]
    public async Task Webhook_SessionCompletedButNotPaid_ChangesNothing()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, paymentId) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_unpaid", "checkout.session.completed", SessionId, "unpaid");

        var response = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentAsync(paymentId)).Status);
    }

    [Fact]
    public async Task Webhook_SessionExpired_MarksThePaymentExpired_AndKeepsTheItemReserved()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, paymentId) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_expired", "checkout.session.expired", SessionId, "unpaid");

        var response = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PaymentStatus.Expired, (await GetPaymentAsync(paymentId)).Status);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task Webhook_PaidSessionMatchingNoPayment_Returns200_AndChangesNothing()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, paymentId) = await ArrangePendingPaymentAsync(seller, buyer);
        var payload = EventJson("evt_stranger", "checkout.session.completed", "cs_test_unknown", "paid");

        var response = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentAsync(paymentId)).Status);
    }

    [Fact]
    public async Task Webhook_UnhandledEventType_Returns200()
    {
        var payload = EventJson("evt_other", "charge.succeeded", SessionId, "paid");

        var response = await PostWebhookAsync(payload, Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- Reservation expiry ----------

    private async Task<int> ExpireAsync(TimeSpan olderThan)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IReservationService>();
        return await service.ExpireStaleAsync(DateTime.UtcNow - olderThan, CancellationToken.None);
    }

    private Task AgeReservationAsync(Guid offerId, TimeSpan age) =>
        WithDbAsync(async db =>
        {
            var offer = await db.Offers.SingleAsync(o => o.Id == offerId);
            offer.RespondedAt = DateTime.UtcNow - age;
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Expiry_ReleasesAReservationOlderThanTheDuration()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        await AgeReservationAsync(offer.Id, TimeSpan.FromHours(49));

        var released = await ExpireAsync(TimeSpan.FromHours(48));

        Assert.Equal(1, released);
        Assert.Equal(ItemStatus.Available, await GetItemStatusAsync(item.Id));
        var storedOffer = await WithDbAsync(db => db.Offers.AsNoTracking().SingleAsync(o => o.Id == offer.Id));
        Assert.Equal(OfferStatus.Cancelled, storedOffer.Status);
    }

    [Fact]
    public async Task Expiry_LeavesARecentReservationAlone()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        await AgeReservationAsync(offer.Id, TimeSpan.FromHours(47));

        var released = await ExpireAsync(TimeSpan.FromHours(48));

        Assert.Equal(0, released);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task Expiry_NeverTouchesAnItemThatWasAlreadySold()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        await AgeReservationAsync(offer.Id, TimeSpan.FromDays(10));
        await SetItemStatusAsync(item.Id, ItemStatus.Sold);

        var released = await ExpireAsync(TimeSpan.FromHours(48));

        Assert.Equal(0, released);
        Assert.Equal(ItemStatus.Sold, await GetItemStatusAsync(item.Id));
    }
}
