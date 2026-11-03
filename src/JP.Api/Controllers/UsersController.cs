using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JP.Api.Data;
using JP.Api.Dtos;
using JP.Api.Entities;

namespace JP.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db) : ControllerBase
{
    // GET /api/users/{id}  (public: no email, no private data)
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SellerProfileDto>> GetProfile(Guid id)
    {
        var profile = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new SellerProfileDto(
                u.Id, u.UserName!, u.City, u.CreatedAt,
                u.Items.Count(i => i.Status == ItemStatus.Available)))
            .FirstOrDefaultAsync();

        return profile is null ? NotFound() : profile;
    }

    // GET /api/users/me/favorites?page=1&pageSize=20  (most recently saved first)
    // "me" never clashes with {id:guid} above: it is not a valid Guid.
    [Authorize]
    [HttpGet("me/favorites")]
    public async Task<ActionResult<PagedResult<ItemSummaryDto>>> GetMyFavorites(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var favorites = db.Favorites.AsNoTracking().Where(f => f.UserId == userId);

        var total = await favorites.CountAsync(ct);
        var page = await favorites
            .OrderByDescending(f => f.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new ItemSummaryDto(
                f.Item.Id, f.Item.Title, f.Item.Price, f.Item.Brand, f.Item.Size,
                f.Item.Condition, f.Item.Status,
                f.Item.Photos.OrderBy(p => p.Position).Select(p => p.Url).FirstOrDefault(),
                f.Item.Seller.UserName!, f.Item.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<ItemSummaryDto>(page, query.Page, query.PageSize, total);
    }

    // Id of the logged-in user ("sub" claim), null if missing or malformed
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
