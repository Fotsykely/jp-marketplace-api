using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using JP.Api.Dtos;
using JP.Api.IntegrationTests.Infrastructure;

namespace JP.Api.IntegrationTests;

/// <summary>Register, login and "who am I" through the real API and database.</summary>
public class AuthTests(IntegrationFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Password = "Password123!";

    private static RegisterRequest NewRegistration(
        string userName = "alice", string? email = null, string password = Password) =>
        new(userName, email ?? $"{userName}@example.com", password, "Lyon");

    // ---------- Register ----------

    [Fact]
    public async Task Register_WithValidData_Returns201_AToken_AndNoPassword()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register", NewRegistration());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
        Assert.Equal("alice", auth.User.UserName);
        Assert.Equal("alice@example.com", auth.User.Email);
        Assert.Equal("Lyon", auth.User.City);

        // The response must never leak the password or its hash
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_StoresAHashedPassword_NotThePlainOne()
    {
        await CreateClient().PostAsJsonAsync("/api/auth/register", NewRegistration());

        var stored = await WithDbAsync(db => Task.FromResult(db.Users.Single().PasswordHash));

        Assert.NotNull(stored);
        Assert.NotEqual(Password, stored);
    }

    [Fact]
    public async Task Register_ReturnedToken_IsAcceptedByProtectedEndpoints()
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns400()
    {
        await RegisterUserAsync("alice");

        var response = await CreateClient().PostAsJsonAsync("/api/auth/register",
            NewRegistration("someone-else", email: "alice@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateUserName_Returns400()
    {
        await RegisterUserAsync("alice");

        var response = await CreateClient().PostAsJsonAsync("/api/auth/register",
            NewRegistration("alice", email: "other@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Short1!")]       // 7 characters: under the 8 minimum
    [InlineData("password123!")]  // no uppercase letter
    [InlineData("Password!!!!")]  // no digit
    public async Task Register_WithWeakPassword_Returns400(string password)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register",
            NewRegistration(password: password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("ab", "ab@example.com")]            // user name shorter than 3
    [InlineData("alice", "not-an-email")]           // invalid email
    public async Task Register_WithInvalidFields_Returns400(string userName, string email)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register",
            NewRegistration(userName, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Login ----------

    [Theory]
    [InlineData("alice")]               // by user name
    [InlineData("alice@example.com")]   // by email
    public async Task Login_WithValidCredentials_Returns200_AndAWorkingToken(string login)
    {
        await RegisterUserAsync("alice");
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.Equal("alice", auth.User.UserName);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        await RegisterUserAsync("alice");

        var response = await CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest("alice", "WrongPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUser_Returns401_WithTheSameMessageAsAWrongPassword()
    {
        await RegisterUserAsync("alice");
        var client = CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("alice", "WrongPassword1!"));
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("ghost", Password));

        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        // Same answer in both cases: the API must not reveal which accounts exist
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_WithMissingFields_Returns400()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Me ----------

    [Fact]
    public async Task Me_ReturnsTheLoggedInAccount()
    {
        var alice = await RegisterUserAsync("alice");

        var response = await alice.Client.GetAsync("/api/auth/me");

        var me = (await response.Content.ReadFromJsonAsync<UserDto>(Json))!;
        Assert.Equal(alice.Id, me.Id);
        Assert.Equal("alice@example.com", me.Email);
    }

    [Fact]
    public async Task Me_WithGarbageToken_Returns401()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithTokenOfADeletedUser_Returns401()
    {
        var alice = await RegisterUserAsync("alice");
        await WithDbAsync(async db =>
        {
            db.Users.RemoveRange(db.Users);
            await db.SaveChangesAsync();
        });

        var response = await alice.Client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- Public profile ----------

    [Fact]
    public async Task PublicProfile_IsAnonymous_AndNeverExposesTheEmail()
    {
        var alice = await RegisterUserAsync("alice");

        var response = await CreateClient().GetAsync($"/api/users/{alice.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("alice", body);
        Assert.DoesNotContain("alice@example.com", body);
        Assert.DoesNotContain("email", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublicProfile_OfUnknownUser_Returns404()
    {
        var response = await CreateClient().GetAsync($"/api/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
