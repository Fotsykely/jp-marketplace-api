using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JP.Api.Dtos;
using JP.Api.Services;

namespace JP.Api.Controllers;

// The business rules live in PaymentService; this controller only maps HTTP <-> service.
[ApiController]
[Authorize]
public class PaymentsController(IPaymentService payments) : ApiControllerBase
{
    // POST /api/items/{id}/checkout   (buyer of the accepted offer only)
    // Returns the Stripe payment page URL; the front end redirects the buyer there.
    [HttpPost("api/items/{id:guid}/checkout")]
    public async Task<ActionResult<CheckoutResponse>> Checkout(Guid id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var result = await payments.CreateCheckoutAsync(id, userId, ct);
        return result.Error is null ? result.Value! : ToError(result.Error.Value);
    }

    private ActionResult ToError(PaymentError error) => error switch
    {
        PaymentError.NotFound => NotFound(),
        PaymentError.NotBuyer => Forbid(),
        PaymentError.ItemUnavailable =>
            Conflict(new ProblemDetails { Title = "Cet article n'est pas réservé : paiement impossible." }),
        PaymentError.AlreadyPaid =>
            Conflict(new ProblemDetails { Title = "Le paiement a déjà été effectué, confirmation en cours." }),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };
}
