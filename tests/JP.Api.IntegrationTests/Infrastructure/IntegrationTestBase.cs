using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using JP.Api.Data;
using JP.Api.Dtos;
using JP.Api.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace JP.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Base class of the integration tests: gives a clean database before each test
/// and small helpers so the tests read like a scenario.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTestBase(IntegrationFixture fixture) : IAsyncLifetime
{
    // Same JSON rules as the API: enums as text ("Reserved" instead of 1)
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private int _userCounter;

    protected IntegrationFixture Fixture => fixture;

    /// <summary>A client with no token (an anonymous visitor).</summary>
    protected HttpClient CreateClient() => fixture.Factory.CreateClient();

    // xUnit creates a new instance of the test class for EVERY test, so this runs before each one
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Registers a new user through the API and returns a client that sends their token.</summary>
    protected async Task<TestUser> RegisterUserAsync(string? userName = null)
    {
        userName ??= $"user{++_userCounter}";
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(userName, $"{userName}@example.com", "Password123!", "Lyon"));
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return new TestUser(auth.User.Id, userName, client);
    }

    /// <summary>Runs code with a scoped <see cref="AppDbContext"/>, to arrange or inspect the database directly.</summary>
    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WithDbAsync<object?>(async db => { await action(db); return null; });

    /// <summary>Inserts a category straight in the database (the demo seed does not run in tests).</summary>
    protected Task<int> CreateCategoryAsync(string name = "Robes", int? parentId = null) =>
        WithDbAsync(async db =>
        {
            var category = new Category { Name = name, Slug = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}", ParentId = parentId };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            return category.Id;
        });

    /// <summary>Creates a listing through the API as the given seller.</summary>
    protected async Task<ItemDetailDto> CreateItemAsync(
        TestUser seller, int categoryId, decimal price = 50m, string title = "Robe fleurie")
    {
        var response = await seller.Client.PostAsJsonAsync("/api/items",
            new CreateItemRequest(title, "Une belle robe en très bon état.", price, "Zara", "M",
                ItemCondition.VeryGood, categoryId, null), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ItemDetailDto>(Json))!;
    }
}

/// <summary>A registered user plus an HTTP client already carrying their token.</summary>
public record TestUser(Guid Id, string UserName, HttpClient Client);
