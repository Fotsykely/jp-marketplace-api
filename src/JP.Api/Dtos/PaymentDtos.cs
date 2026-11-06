namespace JP.Api.Dtos;

/// <summary>Where to send the buyer so they can pay (Stripe's hosted payment page).</summary>
public record CheckoutResponse(string Url);
