namespace JP.Api.Services;

/// <summary>Bound to the "Jwt" section of appsettings.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Key { get; set; }
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public int ExpirationMinutes { get; set; } = 60;
}
