using JP.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace JP.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Shared by every integration test: ONE PostgreSQL container and ONE API instance for the whole run
/// (starting a container takes seconds, so we do not do it per test).
/// xUnit creates it before the first test and disposes it after the last one.
/// </summary>
public class IntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .Build();

    private Respawner _respawner = null!;

    public JpWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Apply the real EF Core migrations, exactly like the app does in Development
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        // Respawn learns the table graph once, then can empty every table quickly (in FK order)
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"]
        });

        Factory = new JpWebApplicationFactory(_postgres.GetConnectionString());
    }

    /// <summary>Empties all tables so each test starts from a blank database.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
