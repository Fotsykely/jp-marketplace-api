using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JP.Api.Services;

namespace JP.Api.Controllers;

[ApiController]
public class StripeWebhookController(IPaymentService payments) : ControllerBase
{
    // POST /api/stripe/webhook
    // Called by Stripe's servers, not by a logged-in user: no JWT, the Stripe-Signature header
    // is the proof of identity. The signature is computed on the exact bytes Stripe sent, so we
    // read the raw body ourselves instead of letting model binding parse it.
    [AllowAnonymous]
    [HttpPost("api/stripe/webhook")]
    public async Task<IActionResult> Handle(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(ct);

        var accepted = await payments.HandleWebhookAsync(
            payload, Request.Headers["Stripe-Signature"].FirstOrDefault(), ct);

        return accepted ? Ok() : BadRequest();
    }
}
