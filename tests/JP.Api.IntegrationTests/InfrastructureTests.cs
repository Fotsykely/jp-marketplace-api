using System.Net;
using System.Net.Http.Json;
using JP.Api.Dtos;
using JP.Api.IntegrationTests.Infrastructure;

namespace JP.Api.IntegrationTests;

/// <summary>Proves the test infrastructure itself works: container, migrations, JWT, cleanup.</summary>
public class InfrastructureTests(IntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Api_Starts_AndAnswersAnonymousRequests()
    {
        var response = await CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Registered_User_CanCallProtectedEndpoint()
    {
        var alice = await RegisterUserAsync("alice");

        var response = await alice.Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<UserDto>(Json);
        Assert.Equal("alice", me!.UserName);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var response = await CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // These two tests both register "bob". If the database were not emptied between tests,
    // whichever runs second would fail on the duplicate user name.
    [Fact]
    public async Task Database_IsEmptyAtTheStartOfEachTest_First() => await RegisterUserAsync("bob");

    [Fact]
    public async Task Database_IsEmptyAtTheStartOfEachTest_Second() => await RegisterUserAsync("bob");

    [Fact]
    public async Task Helpers_CanCreateAnItemEndToEnd()
    {
        var seller = await RegisterUserAsync();
        var categoryId = await CreateCategoryAsync();

        var item = await CreateItemAsync(seller, categoryId, price: 42m);

        Assert.Equal(42m, item.Price);
        Assert.Equal(seller.Id, item.Seller.Id);
    }
}
