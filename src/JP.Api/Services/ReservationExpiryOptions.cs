namespace JP.Api.Services;

/// <summary>Bound to the "ReservationExpiry" section of appsettings.</summary>
public class ReservationExpiryOptions
{
    public const string SectionName = "ReservationExpiry";

    /// <summary>How long an item stays reserved before it goes back on sale.</summary>
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(48);

    /// <summary>How often the background worker looks for stale reservations.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}
