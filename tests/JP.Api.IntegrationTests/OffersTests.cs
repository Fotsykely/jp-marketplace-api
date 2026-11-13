using System.Net;
using System.Net.Http.Json;
using JP.Api.Dtos;
using JP.Api.Entities;
using JP.Api.IntegrationTests.Infrastructure;

namespace JP.Api.IntegrationTests;

/// <summary>Price offers and the reservation they trigger: Available -> Reserved -> Available.</summary>
public class OffersTests(IntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private Task<HttpResponseMessage> PostOfferAsync(TestUser buyer, Guid itemId, decimal amount) =>
        buyer.Client.PostAsJsonAsync($"/api/items/{itemId}/offers", new CreateOfferRequest(amount), Json);

    private static Task<PagedResult<OfferDto>> ReadPageAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<PagedResult<OfferDto>>(Json)!;

    // ---------- Make an offer ----------

    [Fact]
    public async Task MakeOffer_BelowTheAskingPrice_Returns201_AsPending()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);

        var response = await PostOfferAsync(buyer, item.Id, 35m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var offer = (await response.Content.ReadFromJsonAsync<OfferDto>(Json))!;
        Assert.Equal(OfferStatus.Pending, offer.Status);
        Assert.Equal(35m, offer.Amount);
        Assert.Equal(50m, offer.ItemPrice);
        Assert.Equal(buyer.Id, offer.BuyerId);
        Assert.Null(offer.RespondedAt);
        // Making an offer does not reserve anything
        Assert.Equal(ItemStatus.Available, await GetItemStatusAsync(item.Id));
    }

    [Theory]
    [InlineData(50)]   // equal to the asking price
    [InlineData(60)]   // above it
    public async Task MakeOffer_AtOrAboveTheAskingPrice_Returns400(decimal amount)
    {
        var seller = await RegisterUserAsync();
        var buyer = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);

        var response = await PostOfferAsync(buyer, item.Id, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task MakeOffer_WithANonPositiveAmount_Returns400(decimal amount)
    {
        var seller = await RegisterUserAsync();
        var buyer = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);

        var response = await PostOfferAsync(buyer, item.Id, amount);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MakeOffer_OnYourOwnListing_Returns409()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);

        var response = await PostOfferAsync(seller, item.Id, 30m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MakeOffer_OnUnknownItem_Returns404_AndWithoutTokenReturns401()
    {
        var buyer = await RegisterUserAsync();

        var unknown = await PostOfferAsync(buyer, Guid.NewGuid(), 10m);
        var anonymous = await CreateClient().PostAsJsonAsync($"/api/items/{Guid.NewGuid()}/offers",
            new CreateOfferRequest(10m), Json);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Theory]
    [InlineData(ItemStatus.Reserved)]
    [InlineData(ItemStatus.Sold)]
    public async Task MakeOffer_OnAnItemThatIsNotAvailable_Returns409(ItemStatus status)
    {
        var seller = await RegisterUserAsync();
        var buyer = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        await SetItemStatusAsync(item.Id, status);

        var response = await PostOfferAsync(buyer, item.Id, 30m);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MakeOffer_TwiceWhileOneIsPending_Returns409_ButAnotherBuyerCanOffer()
    {
        var seller = await RegisterUserAsync("alice");
        var bob = await RegisterUserAsync("bob");
        var carol = await RegisterUserAsync("carol");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        await MakeOfferAsync(bob, item.Id, 30m);

        var second = await PostOfferAsync(bob, item.Id, 35m);
        var otherBuyer = await PostOfferAsync(carol, item.Id, 32m);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherBuyer.StatusCode);
    }

    [Fact]
    public async Task MakeOffer_AgainAfterTheFirstWasDeclined_IsAllowed()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var first = await MakeOfferAsync(buyer, item.Id, 30m);
        await seller.Client.PostAsync($"/api/offers/{first.Id}/decline", null);

        var retry = await PostOfferAsync(buyer, item.Id, 40m);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    // ---------- List ----------

    [Fact]
    public async Task ListForItem_IsReservedToTheSeller()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        await MakeOfferAsync(buyer, item.Id, 30m);

        var bySeller = await seller.Client.GetAsync($"/api/items/{item.Id}/offers");
        var byBuyer = await buyer.Client.GetAsync($"/api/items/{item.Id}/offers");
        var anonymous = await CreateClient().GetAsync($"/api/items/{item.Id}/offers");
        var unknown = await seller.Client.GetAsync($"/api/items/{Guid.NewGuid()}/offers");

        Assert.Equal(HttpStatusCode.OK, bySeller.StatusCode);
        Assert.Equal(1, (await ReadPageAsync(bySeller)).TotalCount);
        Assert.Equal(HttpStatusCode.Forbidden, byBuyer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task ListMine_OnlyContainsMyOffers_MostRecentFirst()
    {
        var seller = await RegisterUserAsync("alice");
        var bob = await RegisterUserAsync("bob");
        var carol = await RegisterUserAsync("carol");
        var categoryId = await CreateCategoryAsync();
        var first = await CreateItemAsync(seller, categoryId, price: 50m, title: "Premier article");
        var second = await CreateItemAsync(seller, categoryId, price: 50m, title: "Second article");
        await MakeOfferAsync(bob, first.Id, 30m);
        await MakeOfferAsync(bob, second.Id, 31m);
        await MakeOfferAsync(carol, first.Id, 32m);

        var mine = await ReadPageAsync(await bob.Client.GetAsync("/api/users/me/offers"));

        Assert.Equal(2, mine.TotalCount);
        Assert.Equal(["Second article", "Premier article"], mine.Items.Select(o => o.ItemTitle));
        Assert.All(mine.Items, o => Assert.Equal(bob.Id, o.BuyerId));
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateClient().GetAsync("/api/users/me/offers")).StatusCode);
    }

    // ---------- Accept ----------

    [Fact]
    public async Task Accept_ReservesTheItem_AndDeclinesTheOtherPendingOffers()
    {
        var seller = await RegisterUserAsync("alice");
        var bob = await RegisterUserAsync("bob");
        var carol = await RegisterUserAsync("carol");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var bobsOffer = await MakeOfferAsync(bob, item.Id, 40m);
        var carolsOffer = await MakeOfferAsync(carol, item.Id, 35m);

        var response = await seller.Client.PostAsync($"/api/offers/{bobsOffer.Id}/accept", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<OfferDto>(Json))!;
        Assert.Equal(OfferStatus.Accepted, accepted.Status);
        Assert.NotNull(accepted.RespondedAt);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));

        var all = await ReadPageAsync(await seller.Client.GetAsync($"/api/items/{item.Id}/offers"));
        Assert.Equal(OfferStatus.Accepted, all.Items.Single(o => o.Id == bobsOffer.Id).Status);
        Assert.Equal(OfferStatus.Declined, all.Items.Single(o => o.Id == carolsOffer.Id).Status);
    }

    [Fact]
    public async Task Accept_ByANonSeller_Returns403_AndNothingChanges()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var offer = await MakeOfferAsync(buyer, item.Id, 40m);

        // Neither the buyer nor a stranger can accept an offer
        var byBuyer = await buyer.Client.PostAsync($"/api/offers/{offer.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Forbidden, byBuyer.StatusCode);
        Assert.Equal(ItemStatus.Available, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task Accept_UnknownOffer_Returns404_AndWithoutTokenReturns401()
    {
        var seller = await RegisterUserAsync();

        var unknown = await seller.Client.PostAsync($"/api/offers/{Guid.NewGuid()}/accept", null);
        var anonymous = await CreateClient().PostAsync($"/api/offers/{Guid.NewGuid()}/accept", null);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Accept_AnOfferAlreadyAnswered_Returns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var offer = await MakeOfferAsync(buyer, item.Id, 40m);
        await AcceptOfferAsync(seller, offer.Id);

        var again = await seller.Client.PostAsync($"/api/offers/{offer.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Accept_WhenTheItemIsNoLongerAvailable_Returns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var offer = await MakeOfferAsync(buyer, item.Id, 40m);
        await SetItemStatusAsync(item.Id, ItemStatus.Sold);

        var response = await seller.Client.PostAsync($"/api/offers/{offer.Id}/accept", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Decline ----------

    [Fact]
    public async Task Decline_BySeller_MarksTheOfferDeclined_AndKeepsTheItemAvailable()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var offer = await MakeOfferAsync(buyer, item.Id, 40m);

        var response = await seller.Client.PostAsync($"/api/offers/{offer.Id}/decline", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var declined = (await response.Content.ReadFromJsonAsync<OfferDto>(Json))!;
        Assert.Equal(OfferStatus.Declined, declined.Status);
        Assert.NotNull(declined.RespondedAt);
        Assert.Equal(ItemStatus.Available, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task Decline_ByANonSeller_Returns403_AndTwiceReturns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 50m);
        var offer = await MakeOfferAsync(buyer, item.Id, 40m);

        var byBuyer = await buyer.Client.PostAsync($"/api/offers/{offer.Id}/decline", null);
        await seller.Client.PostAsync($"/api/offers/{offer.Id}/decline", null);
        var twice = await seller.Client.PostAsync($"/api/offers/{offer.Id}/decline", null);

        Assert.Equal(HttpStatusCode.Forbidden, byBuyer.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    // ---------- Cancel reservation ----------

    [Fact]
    public async Task CancelReservation_PutsTheItemBackOnSale_AndCancelsTheAcceptedOffer()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/cancel-reservation", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(ItemStatus.Available, await GetItemStatusAsync(item.Id));
        var offers = await ReadPageAsync(await seller.Client.GetAsync($"/api/items/{item.Id}/offers"));
        Assert.Equal(OfferStatus.Cancelled, offers.Items.Single(o => o.Id == offer.Id).Status);
        // The item is searchable again, and a new offer is possible
        Assert.Equal(HttpStatusCode.Created, (await PostOfferAsync(buyer, item.Id, 41m)).StatusCode);
    }

    [Fact]
    public async Task CancelReservation_ByANonSeller_Returns403()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, _) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());

        var response = await buyer.Client.PostAsync($"/api/items/{item.Id}/cancel-reservation", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ItemStatus.Reserved, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task CancelReservation_OfAnItemThatIsNotReserved_Returns409()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/cancel-reservation", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CancelReservation_OfASoldItem_Returns409()
    {
        var seller = await RegisterUserAsync("alice");
        var buyer = await RegisterUserAsync("bob");
        var (item, _) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        await SetItemStatusAsync(item.Id, ItemStatus.Sold);

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/cancel-reservation", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ItemStatus.Sold, await GetItemStatusAsync(item.Id));
    }

    [Fact]
    public async Task CancelReservation_OfUnknownItem_Returns404_AndWithoutTokenReturns401()
    {
        var seller = await RegisterUserAsync();

        var unknown = await seller.Client.PostAsync($"/api/items/{Guid.NewGuid()}/cancel-reservation", null);
        var anonymous = await CreateClient().PostAsync($"/api/items/{Guid.NewGuid()}/cancel-reservation", null);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
