using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JP.Api.Dtos;
using JP.Api.Services;

namespace JP.Api.Controllers;

// Routes are spelled out per action: offers are created/listed under an item,
// answered by id, and the buyer's own offers live under /users/me like favourites.
// The business rules live in OfferService; this controller only maps HTTP <-> service.
[ApiController]
[Authorize]
public class OffersController(IOfferService offers) : ApiControllerBase
{
    // POST /api/items/{itemId}/offers   { "amount": 35 }
    [HttpPost("api/items/{itemId:guid}/offers")]
    public async Task<ActionResult<OfferDto>> Create(
        Guid itemId, CreateOfferRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var result = await offers.CreateAsync(itemId, userId, request.Amount, ct);
        return result.Error is null
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : ToError(result.Error.Value);
    }

    // GET /api/items/{itemId}/offers   (seller only: offers received on this listing)
    [HttpGet("api/items/{itemId:guid}/offers")]
    public async Task<ActionResult<PagedResult<OfferDto>>> ListForItem(
        Guid itemId, [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var result = await offers.ListForItemAsync(itemId, userId, query, ct);
        return result.Error is null ? result.Value! : ToError(result.Error.Value);
    }

    // GET /api/users/me/offers   (offers I made, most recent first)
    [HttpGet("api/users/me/offers")]
    public async Task<ActionResult<PagedResult<OfferDto>>> ListMine(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        return await offers.ListMadeByAsync(userId, query, ct);
    }

    // POST /api/offers/{id}/accept   (seller only)
    [HttpPost("api/offers/{id:guid}/accept")]
    public async Task<ActionResult<OfferDto>> Accept(Guid id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var result = await offers.AcceptAsync(id, userId, ct);
        return result.Error is null ? result.Value! : ToError(result.Error.Value);
    }

    // POST /api/offers/{id}/decline   (seller only)
    [HttpPost("api/offers/{id:guid}/decline")]
    public async Task<ActionResult<OfferDto>> Decline(Guid id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var result = await offers.DeclineAsync(id, userId, ct);
        return result.Error is null ? result.Value! : ToError(result.Error.Value);
    }

    // Single place that decides which HTTP status and message goes with each business error
    private ActionResult ToError(OfferError error) => error switch
    {
        OfferError.NotFound => NotFound(),
        OfferError.NotSeller => Forbid(),
        OfferError.AmountTooHigh =>
            InvalidField("amount", "L'offre doit être inférieure au prix de l'article."),
        OfferError.OwnItem =>
            Conflict(Message("Vous ne pouvez pas faire une offre sur votre propre annonce.")),
        OfferError.ItemUnavailable =>
            Conflict(Message("Cet article n'est plus disponible.")),
        OfferError.AlreadyPending =>
            Conflict(Message("Vous avez déjà une offre en attente sur cet article.")),
        OfferError.AlreadyAnswered =>
            Conflict(Message("Cette offre a déjà reçu une réponse.")),
        _ => StatusCode(StatusCodes.Status500InternalServerError)
    };

    private static ProblemDetails Message(string title) => new() { Title = title };
}
