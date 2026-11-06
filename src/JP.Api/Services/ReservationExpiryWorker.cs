using Microsoft.Extensions.Options;

namespace JP.Api.Services;

// A BackgroundService runs for the whole life of the app, next to the HTTP requests.
// It is a singleton, but the DbContext is scoped (one per request), so each cycle
// creates its own scope, like a request would.
public class ReservationExpiryWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ReservationExpiryOptions> options,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.CheckInterval);

        // WaitForNextTickAsync returns false (or throws) when the app is shutting down
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var reservations = scope.ServiceProvider.GetRequiredService<IReservationService>();

                var released = await reservations.ExpireStaleAsync(
                    DateTime.UtcNow - settings.Duration, stoppingToken);
                if (released > 0)
                    logger.LogInformation("Released {Count} expired reservation(s)", released);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                // A transient error (database down...) must not kill the loop for good
                logger.LogError(ex, "Reservation expiry cycle failed");
            }
        }
    }
}
