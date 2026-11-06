namespace JP.Api.Services;

/// <summary>
/// Bound to the "Stripe" section. Only SecretKey and WebhookSecret are secret: they come from
/// <c>dotnet user-secrets</c>, never from a file in the repository.
/// </summary>
public class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Server-side API key (sk_test_... in test mode).</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Signing secret of the webhook endpoint (whsec_...), used to prove a call comes from Stripe.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>ISO 4217 code, lower case. Change it here to switch currency (e.g. "mga").</summary>
    public string Currency { get; set; } = "eur";

    /// <summary>Where Stripe sends the buyer after a successful payment.</summary>
    public string SuccessUrl { get; set; } = "http://localhost:3000/checkout/success";

    /// <summary>Where Stripe sends the buyer if they leave the payment page.</summary>
    public string CancelUrl { get; set; } = "http://localhost:3000/checkout/cancel";
}
