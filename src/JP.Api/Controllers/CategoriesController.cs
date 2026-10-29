using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JP.Api.Data;
using JP.Api.Dtos;

namespace JP.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CategoryDto>> GetAll() =>
        await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.ParentId != null).ThenBy(c => c.ParentId).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.ParentId))
            .ToListAsync();
}
