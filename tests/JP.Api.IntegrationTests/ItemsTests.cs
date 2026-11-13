using System.Net;
using System.Net.Http.Json;
using JP.Api.Dtos;
using JP.Api.Entities;
using JP.Api.IntegrationTests.Infrastructure;

namespace JP.Api.IntegrationTests;

/// <summary>Listings: create, read, search, update, delete, photos and favourites.</summary>
public class ItemsTests(IntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private static CreateItemRequest NewItem(
        int categoryId, string title = "Robe fleurie", decimal price = 50m,
        string? brand = "Zara", ItemCondition condition = ItemCondition.VeryGood) =>
        new(title, "Une belle robe en très bon état.", price, brand, "M", condition, categoryId, null);

    private static UpdateItemRequest NewUpdate(int categoryId, string title = "Robe modifiée", decimal price = 45m) =>
        new(title, "Description modifiée et suffisamment longue.", price, "H&M", "L", ItemCondition.Good, categoryId);

    private async Task<PagedResult<ItemSummaryDto>> SearchAsync(string queryString = "")
    {
        var response = await CreateClient().GetAsync($"/api/items{queryString}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<ItemSummaryDto>>(Json))!;
    }

    // A few bytes starting with the JPEG signature: enough for the API's magic-bytes check
    private static MultipartFormDataContent Upload(byte[] bytes, string fileName = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        return content;
    }

    private static byte[] FakeJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1];
    private static byte[] FakePng() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    // ---------- Create ----------

    [Fact]
    public async Task Create_AsLoggedInUser_Returns201_AndTheSellerIsTheTokenOwner()
    {
        var seller = await RegisterUserAsync("alice");
        var categoryId = await CreateCategoryAsync();

        var response = await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, title: "  Robe fleurie  "), Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var item = (await response.Content.ReadFromJsonAsync<ItemDetailDto>(Json))!;
        Assert.Equal(seller.Id, item.Seller.Id);
        Assert.Equal("Robe fleurie", item.Title); // trimmed
        Assert.Equal(ItemStatus.Available, item.Status);
        Assert.Equal(categoryId, item.Category.Id);
    }

    [Fact]
    public async Task Create_WithoutToken_Returns401()
    {
        var categoryId = await CreateCategoryAsync();

        var response = await CreateClient().PostAsJsonAsync("/api/items", NewItem(categoryId), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithUnknownCategory_Returns400()
    {
        var seller = await RegisterUserAsync();

        var response = await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId: 999_999), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("ab", 50)]      // title too short
    [InlineData("Robe", 0)]     // price must be positive
    [InlineData("Robe", -5)]
    public async Task Create_WithInvalidFields_Returns400(string title, decimal price)
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();

        var response = await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, title, price), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Read ----------

    [Fact]
    public async Task GetById_ReturnsTheItem_Anonymously()
    {
        var seller = await RegisterUserAsync("alice");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync(), price: 42m);

        var response = await CreateClient().GetAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var read = (await response.Content.ReadFromJsonAsync<ItemDetailDto>(Json))!;
        Assert.Equal(42m, read.Price);
        Assert.Equal("alice", read.Seller.UserName);
    }

    [Fact]
    public async Task GetById_WithUnknownId_Returns404()
    {
        var response = await CreateClient().GetAsync($"/api/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Search ----------

    [Fact]
    public async Task Search_OnlyListsAvailableItems()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        var available = await CreateItemAsync(seller, categoryId, title: "Disponible");
        var reserved = await CreateItemAsync(seller, categoryId, title: "Reservee");
        var sold = await CreateItemAsync(seller, categoryId, title: "Vendue");
        await SetItemStatusAsync(reserved.Id, ItemStatus.Reserved);
        await SetItemStatusAsync(sold.Id, ItemStatus.Sold);

        var result = await SearchAsync();

        var only = Assert.Single(result.Items);
        Assert.Equal(available.Id, only.Id);
    }

    [Fact]
    public async Task Search_ByText_MatchesTitleOrBrand_CaseInsensitive()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Robe d'été", brand: "Zara"), Json);
        await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Baskets blanches", brand: "Nike"), Json);
        await seller.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Pull en laine", brand: null), Json);

        var byTitle = await SearchAsync("?q=ROBE");
        var byBrand = await SearchAsync("?q=nike");

        Assert.Equal("Robe d'été", Assert.Single(byTitle.Items).Title);
        Assert.Equal("Baskets blanches", Assert.Single(byBrand.Items).Title);
    }

    [Fact]
    public async Task Search_ByParentCategory_IncludesSubCategories()
    {
        var seller = await RegisterUserAsync();
        var parentId = await CreateCategoryAsync("Femme");
        var childId = await CreateCategoryAsync("Robes", parentId);
        var otherId = await CreateCategoryAsync("Homme");
        var inChild = await CreateItemAsync(seller, childId, title: "Dans la sous-categorie");
        await CreateItemAsync(seller, otherId, title: "Ailleurs");

        var byParent = await SearchAsync($"?categoryId={parentId}");
        var byChild = await SearchAsync($"?categoryId={childId}");

        Assert.Equal(inChild.Id, Assert.Single(byParent.Items).Id);
        Assert.Equal(inChild.Id, Assert.Single(byChild.Items).Id);
    }

    [Fact]
    public async Task Search_ByPriceRangeConditionAndSeller_AppliesEveryFilter()
    {
        var alice = await RegisterUserAsync("alice");
        var bob = await RegisterUserAsync("bob");
        var categoryId = await CreateCategoryAsync();
        await alice.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Cheap", 10m, condition: ItemCondition.Good), Json);
        await alice.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Middle", 30m, condition: ItemCondition.NewWithTags), Json);
        await alice.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Pricey", 90m, condition: ItemCondition.NewWithTags), Json);
        await bob.Client.PostAsJsonAsync("/api/items", NewItem(categoryId, "Bobs", 30m, condition: ItemCondition.NewWithTags), Json);

        var priceRange = await SearchAsync("?minPrice=20&maxPrice=50");
        var condition = await SearchAsync("?condition=Good");
        var seller = await SearchAsync($"?sellerId={bob.Id}");

        Assert.Equal(["Bobs", "Middle"], priceRange.Items.Select(i => i.Title).Order());
        Assert.Equal("Cheap", Assert.Single(condition.Items).Title);
        Assert.Equal("Bobs", Assert.Single(seller.Items).Title);
    }

    [Theory]
    [InlineData("price_asc", new[] { "Item A", "Item B", "Item C" })]
    [InlineData("price_desc", new[] { "Item C", "Item B", "Item A" })]
    [InlineData("newest", new[] { "Item C", "Item A", "Item B" })]   // creation order is B, A, C (newest first)
    [InlineData(null, new[] { "Item C", "Item A", "Item B" })]       // newest is the default
    public async Task Search_Sorting(string? sort, string[] expectedTitles)
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        await CreateItemAsync(seller, categoryId, price: 20m, title: "Item B");
        await CreateItemAsync(seller, categoryId, price: 10m, title: "Item A");
        await CreateItemAsync(seller, categoryId, price: 30m, title: "Item C");

        var result = await SearchAsync(sort is null ? "" : $"?sort={sort}");

        Assert.Equal(expectedTitles.Length, result.TotalCount);
        // "newest" expects the most recent first: C was created last
        Assert.Equal(expectedTitles, result.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Search_IsPaginated()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        for (var i = 1; i <= 3; i++)
            await CreateItemAsync(seller, categoryId, price: i * 10, title: $"Item {i}");

        var page1 = await SearchAsync("?sort=price_asc&page=1&pageSize=2");
        var page2 = await SearchAsync("?sort=price_asc&page=2&pageSize=2");

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(["Item 1", "Item 2"], page1.Items.Select(i => i.Title));
        Assert.Equal(["Item 3"], page2.Items.Select(i => i.Title));
    }

    [Theory]
    [InlineData("?pageSize=101")]
    [InlineData("?pageSize=0")]
    [InlineData("?page=0")]
    public async Task Search_WithInvalidPaging_Returns400(string queryString)
    {
        var response = await CreateClient().GetAsync($"/api/items{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_ByTheOwner_Returns204_AndChangesTheItem()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        var item = await CreateItemAsync(seller, categoryId);

        var response = await seller.Client.PutAsJsonAsync($"/api/items/{item.Id}", NewUpdate(categoryId), Json);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var read = (await CreateClient().GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}", Json))!;
        Assert.Equal("Robe modifiée", read.Title);
        Assert.Equal(45m, read.Price);
        Assert.NotNull(read.UpdatedAt);
    }

    [Fact]
    public async Task Update_ByAnotherUser_Returns403_AndChangesNothing()
    {
        var seller = await RegisterUserAsync("alice");
        var intruder = await RegisterUserAsync("mallory");
        var categoryId = await CreateCategoryAsync();
        var item = await CreateItemAsync(seller, categoryId, title: "Original");

        var response = await intruder.Client.PutAsJsonAsync($"/api/items/{item.Id}", NewUpdate(categoryId), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var read = (await CreateClient().GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}", Json))!;
        Assert.Equal("Original", read.Title);
    }

    [Fact]
    public async Task Update_WithoutToken_Returns401()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        var item = await CreateItemAsync(seller, categoryId);

        var response = await CreateClient().PutAsJsonAsync($"/api/items/{item.Id}", NewUpdate(categoryId), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_OfUnknownItem_Returns404()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();

        var response = await seller.Client.PutAsJsonAsync($"/api/items/{Guid.NewGuid()}", NewUpdate(categoryId), Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(ItemStatus.Reserved)]
    [InlineData(ItemStatus.Sold)]
    public async Task Update_OfAnItemThatIsNotAvailable_Returns409(ItemStatus status)
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();
        var item = await CreateItemAsync(seller, categoryId);
        await SetItemStatusAsync(item.Id, status);

        var response = await seller.Client.PutAsJsonAsync($"/api/items/{item.Id}", NewUpdate(categoryId), Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithUnknownCategory_Returns400()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.PutAsJsonAsync($"/api/items/{item.Id}", NewUpdate(999_999), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_ByTheOwner_Returns204_ThenTheItemIsGone()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.DeleteAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CreateClient().GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_ByAnotherUser_Returns403_AndTheItemStays()
    {
        var seller = await RegisterUserAsync("alice");
        var intruder = await RegisterUserAsync("mallory");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await intruder.Client.DeleteAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CreateClient().GetAsync($"/api/items/{item.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_Returns401()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await CreateClient().DeleteAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfUnknownItem_Returns404()
    {
        var seller = await RegisterUserAsync();

        var response = await seller.Client.DeleteAsync($"/api/items/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfAReservedItem_Returns409()
    {
        var seller = await RegisterUserAsync();
        var buyer = await RegisterUserAsync();
        var (item, _) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());

        var response = await seller.Client.DeleteAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfAnItemWithAPayment_Returns409_BecauseThePaymentHistoryIsKept()
    {
        var seller = await RegisterUserAsync();
        var buyer = await RegisterUserAsync();
        var (item, offer) = await CreateReservedItemAsync(seller, buyer, await CreateCategoryAsync());
        // The reservation was released but a payment attempt exists
        await seller.Client.PostAsync($"/api/items/{item.Id}/cancel-reservation", null);
        await WithDbAsync(async db =>
        {
            db.Payments.Add(new Payment
            {
                ItemId = item.Id, OfferId = offer.Id, BuyerId = buyer.Id,
                Amount = offer.Amount, Currency = "eur", StripeSessionId = "cs_test_history"
            });
            await db.SaveChangesAsync();
        });

        var response = await seller.Client.DeleteAsync($"/api/items/{item.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Photos ----------

    [Fact]
    public async Task Photo_Upload_ByTheOwner_Returns201_AndAppearsOnTheItem()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = (await response.Content.ReadFromJsonAsync<PhotoDto>(Json))!;
        Assert.EndsWith(".jpg", photo.Url);
        Assert.Equal(0, photo.Position);

        var read = (await CreateClient().GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}", Json))!;
        Assert.Equal([photo.Url], read.PhotoUrls);
    }

    [Fact]
    public async Task Photo_Upload_DetectsTheFormatFromTheBytes_NotFromTheFileName()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        // A PNG that claims to be a .jpg
        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakePng(), "liar.jpg"));

        var photo = (await response.Content.ReadFromJsonAsync<PhotoDto>(Json))!;
        Assert.EndsWith(".png", photo.Url);
    }

    [Fact]
    public async Task Photo_Upload_OfANonImage_Returns400()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        var notAnImage = "this is definitely not an image"u8.ToArray();

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(notAnImage, "virus.jpg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Photo_Upload_OfAnEmptyFile_Returns400()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Photo_Upload_OfAFileOver5MB_IsRefused()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        var tooBig = new byte[5 * 1024 * 1024 + 1];
        FakeJpeg().CopyTo(tooBig, 0);

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(tooBig));

        // 400 from our own check, or 413 if the request size limit triggers first
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Unexpected status {response.StatusCode}");
    }

    [Fact]
    public async Task Photo_Upload_ByAnotherUser_Returns403()
    {
        var seller = await RegisterUserAsync("alice");
        var intruder = await RegisterUserAsync("mallory");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await intruder.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Photo_Upload_WithoutToken_Returns401()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await CreateClient().PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Photo_Upload_OnAReservedItem_Returns409()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        await SetItemStatusAsync(item.Id, ItemStatus.Reserved);

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Photo_Upload_StopsAtEightPhotos()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        for (var i = 0; i < 8; i++)
            (await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()))).EnsureSuccessStatusCode();

        var ninth = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));

        Assert.Equal(HttpStatusCode.BadRequest, ninth.StatusCode);
    }

    [Fact]
    public async Task Photo_List_ReturnsThePhotosInOrder_AndIs404ForAnUnknownItem()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));
        await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakePng()));

        var photos = (await CreateClient().GetFromJsonAsync<List<PhotoDto>>($"/api/items/{item.Id}/photos", Json))!;
        var unknown = await CreateClient().GetAsync($"/api/items/{Guid.NewGuid()}/photos");

        Assert.Equal([0, 1], photos.Select(p => p.Position));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Photo_Delete_ByTheOwner_Returns204_AndByAnotherUserReturns403()
    {
        var seller = await RegisterUserAsync("alice");
        var intruder = await RegisterUserAsync("mallory");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        var upload = await seller.Client.PostAsync($"/api/items/{item.Id}/photos", Upload(FakeJpeg()));
        var photo = (await upload.Content.ReadFromJsonAsync<PhotoDto>(Json))!;

        var byIntruder = await intruder.Client.DeleteAsync($"/api/items/{item.Id}/photos/{photo.Id}");
        var byOwner = await seller.Client.DeleteAsync($"/api/items/{item.Id}/photos/{photo.Id}");
        var again = await seller.Client.DeleteAsync($"/api/items/{item.Id}/photos/{photo.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byIntruder.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        var read = (await CreateClient().GetFromJsonAsync<ItemDetailDto>($"/api/items/{item.Id}", Json))!;
        Assert.Empty(read.PhotoUrls);
    }

    // ---------- Favourites ----------

    [Fact]
    public async Task Favorite_Add_IsIdempotent_AndListedMostRecentFirst()
    {
        var seller = await RegisterUserAsync("alice");
        var fan = await RegisterUserAsync("bob");
        var categoryId = await CreateCategoryAsync();
        var first = await CreateItemAsync(seller, categoryId, title: "Premier");
        var second = await CreateItemAsync(seller, categoryId, title: "Second");

        Assert.Equal(HttpStatusCode.NoContent, (await fan.Client.PostAsync($"/api/items/{first.Id}/favorite", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await fan.Client.PostAsync($"/api/items/{first.Id}/favorite", null)).StatusCode);
        await fan.Client.PostAsync($"/api/items/{second.Id}/favorite", null);

        var list = (await fan.Client.GetFromJsonAsync<PagedResult<ItemSummaryDto>>("/api/users/me/favorites", Json))!;
        Assert.Equal(2, list.TotalCount);
        Assert.Equal(["Second", "Premier"], list.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Favorite_OnYourOwnListing_Returns409()
    {
        var seller = await RegisterUserAsync();
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());

        var response = await seller.Client.PostAsync($"/api/items/{item.Id}/favorite", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Favorite_OnUnknownItem_Returns404_AndWithoutTokenReturns401()
    {
        var fan = await RegisterUserAsync();

        var unknown = await fan.Client.PostAsync($"/api/items/{Guid.NewGuid()}/favorite", null);
        var anonymous = await CreateClient().PostAsync($"/api/items/{Guid.NewGuid()}/favorite", null);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Favorite_Remove_IsIdempotent_AndEmptiesTheList()
    {
        var seller = await RegisterUserAsync("alice");
        var fan = await RegisterUserAsync("bob");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        await fan.Client.PostAsync($"/api/items/{item.Id}/favorite", null);

        var first = await fan.Client.DeleteAsync($"/api/items/{item.Id}/favorite");
        var second = await fan.Client.DeleteAsync($"/api/items/{item.Id}/favorite");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var list = (await fan.Client.GetFromJsonAsync<PagedResult<ItemSummaryDto>>("/api/users/me/favorites", Json))!;
        Assert.Empty(list.Items);
    }

    [Fact]
    public async Task Favorites_AreNotSharedBetweenUsers()
    {
        var seller = await RegisterUserAsync("alice");
        var fan = await RegisterUserAsync("bob");
        var other = await RegisterUserAsync("carol");
        var item = await CreateItemAsync(seller, await CreateCategoryAsync());
        await fan.Client.PostAsync($"/api/items/{item.Id}/favorite", null);

        var list = (await other.Client.GetFromJsonAsync<PagedResult<ItemSummaryDto>>("/api/users/me/favorites", Json))!;

        Assert.Empty(list.Items);
    }
}
