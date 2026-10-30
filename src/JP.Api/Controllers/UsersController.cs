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
}
