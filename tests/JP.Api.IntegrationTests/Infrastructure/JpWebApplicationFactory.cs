using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JP.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Starts the real API in memory (no network port) with test settings.
/// <c>WebApplicationFactory&lt;Program&gt;</c> runs Program.cs, so the tests go through the same
/// controllers, services, EF Core and JWT validation as production.
/// </summary>
public class JpWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string JwtKey = "integration-tests-signing-key-0123456789-abcdef";
    public const string StripeWebhookSecret = "whsec_integration_tests_secret";

    private readonly string _photoDirectory =
        Path.Combine(Path.GetTempPath(), "jp-api-tests-uploads-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that environment would run the migrations and the demo seed against
        // the developer's own database. The fixture migrates the test container itself.
        builder.UseEnvironment("Testing");

        // UseSetting is read by Program.cs even for the values it reads eagerly (like Jwt:Key).
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Stripe:SecretKey", "sk_test_not_used_in_tests");
        builder.UseSetting("Stripe:WebhookSecret", StripeWebhookSecret);

        // The expiry worker must never wake up in the middle of a test and touch our data
        builder.UseSetting("ReservationExpiry:CheckInterval", "24:00:00");

        // Uploaded photos go to a temporary folder, not into the project
        builder.UseSetting("PhotoStorage:Directory", _photoDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_photoDirectory))
            Directory.Delete(_photoDirectory, recursive: true);
    }
}
