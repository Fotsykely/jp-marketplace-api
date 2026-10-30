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
[Route("api/items")]
public class ItemsController(AppDbContext db) : ControllerBase
{
    // GET /api/items?q=dress&categoryId=3&minPrice=10000&sort=price_asc&page=1
    [HttpGet]
    public async Task<PagedResult<ItemSummaryDto>> Search([FromQuery] ItemQuery query)
    {
        var items = db.Items.AsNoTracking().Where(i => i.Status == ItemStatus.Available);

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{query.Q.Trim()}%";
            items = items.Where(i => EF.Functions.ILike(i.Title, pattern)
                                  || (i.Brand != null && EF.Functions.ILike(i.Brand, pattern)));
        }
        if (query.CategoryId is int catId)
            items = items.Where(i => i.CategoryId == catId || i.Category.ParentId == catId);
        if (query.MinPrice is decimal min) items = items.Where(i => i.Price >= min);
        if (query.MaxPrice is decimal max) items = items.Where(i => i.Price <= max);
        if (query.Condition is ItemCondition cond) items = items.Where(i => i.Condition == cond);
        if (query.SellerId is Guid sellerId) items = items.Where(i => i.SellerId == sellerId);

        items = query.Sort switch
        {
            "price_asc" => items.OrderBy(i => i.Price),
            "price_desc" => items.OrderByDescending(i => i.Price),
            _ => items.OrderByDescending(i => i.CreatedAt)
        };

        var total = await items.CountAsync();
        var page = await items
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(i => new ItemSummaryDto(
                i.Id, i.Title, i.Price, i.Brand, i.Size, i.Condition, i.Status,
                i.Photos.OrderBy(p => p.Position).Select(p => p.Url).FirstOrDefault(),
                i.Seller.UserName!, i.CreatedAt))
            .ToListAsync();

        return new PagedResult<ItemSummaryDto>(page, query.Page, query.PageSize, total);
    }

    // GET /api/items/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ItemDetailDto>> GetById(Guid id)
    {
        var item = await db.Items
            .AsNoTracking()
            .Where(i => i.Id == id)
            .Select(i => new ItemDetailDto(
                i.Id, i.Title, i.Description, i.Price, i.Brand, i.Size, i.Condition, i.Status,
                new CategoryDto(i.Category.Id, i.Category.Name, i.Category.Slug, i.Category.ParentId),
                new SellerDto(i.Seller.Id, i.Seller.UserName!, i.Seller.City),
                i.Photos.OrderBy(p => p.Position).Select(p => p.Url).ToList(),
                i.CreatedAt, i.UpdatedAt))
            .FirstOrDefaultAsync();

        return item is null ? NotFound() : item;
    }

    // POST /api/items
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ItemDetailDto>> Create(CreateItemRequest req)
    {
        // The seller is the logged-in user: "sub" claim of the token, never the request body
        if (CurrentUserId is not Guid sellerId) return Unauthorized();

        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return InvalidField(nameof(req.CategoryId), "Catégorie inconnue");

        var item = new Item
        {
            Title = req.Title.Trim(),
            Description = req.Description.Trim(),
            Price = req.Price,
            Brand = req.Brand,
            Size = req.Size,
            Condition = req.Condition,
            CategoryId = req.CategoryId,
            SellerId = sellerId,
            Photos = (req.PhotoUrls ?? [])
                .Select((url, idx) => new ItemPhoto { Url = url, Position = idx })
                .ToList()
        };

        db.Items.Add(item);
        await db.SaveChangesAsync();

        var created = (await GetById(item.Id)).Value!;
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, created);
    }

    // PUT /api/items/{id}
    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateItemRequest req)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        // Authenticated is not enough: only the seller can edit their own item
        if (item.SellerId != userId) return Forbid();
        if (item.Status == ItemStatus.Sold)
            return Conflict(new ProblemDetails { Title = "Un article vendu ne peut plus être modifié." });
        if (!await db.Categories.AnyAsync(c => c.Id == req.CategoryId))
            return InvalidField(nameof(req.CategoryId), "Catégorie inconnue");

        item.Title = req.Title.Trim();
        item.Description = req.Description.Trim();
        item.Price = req.Price;
        item.Brand = req.Brand;
        item.Size = req.Size;
        item.Condition = req.Condition;
        item.CategoryId = req.CategoryId;
        item.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/items/{id}
    [Authorize]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (CurrentUserId is not Guid userId) return Unauthorized();

        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        // Authenticated is not enough: only the seller can delete their own item
        if (item.SellerId != userId) return Forbid();

        db.Items.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Id of the logged-in user ("sub" claim), null if missing or malformed
    private Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private ActionResult InvalidField(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
