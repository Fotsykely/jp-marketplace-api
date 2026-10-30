using System.ComponentModel.DataAnnotations;
using JP.Api.Entities;

namespace JP.Api.Dtos;

// DTOs separate what the API exposes from what is stored in the database
// (same principle as DTOs/records in Spring Boot).

public record ItemSummaryDto(
    Guid Id, string Title, decimal Price, string? Brand, string? Size,
    ItemCondition Condition, ItemStatus Status, string? CoverPhotoUrl,
    string SellerUserName, DateTime CreatedAt);

public record ItemDetailDto(
    Guid Id, string Title, string Description, decimal Price, string? Brand, string? Size,
    ItemCondition Condition, ItemStatus Status, CategoryDto Category, SellerDto Seller,
    IReadOnlyList<string> PhotoUrls, DateTime CreatedAt, DateTime? UpdatedAt);

public record SellerDto(Guid Id, string UserName, string? City);

/// <summary>Public face of a seller. Deliberately no email or other private data.</summary>
public record SellerProfileDto(Guid Id, string UserName, string? City, DateTime MemberSince, int AvailableItemsCount);

public record CategoryDto(int Id, string Name, string Slug, int? ParentId);

public record CreateItemRequest(
    [Required, StringLength(100, MinimumLength = 3)] string Title,
    [Required, StringLength(2000, MinimumLength = 10)] string Description,
    [Range(0.01, 100_000_000)] decimal Price,
    [StringLength(50)] string? Brand,
    [StringLength(20)] string? Size,
    ItemCondition Condition,
      int CategoryId,
    List<string>? PhotoUrls);

public record UpdateItemRequest(
    [Required, StringLength(100, MinimumLength = 3)] string Title,
    [Required, StringLength(2000, MinimumLength = 10)] string Description,
    [Range(0.01, 100_000_000)] decimal Price,
    [StringLength(50)] string? Brand,
    [StringLength(20)] string? Size,
    ItemCondition Condition,
    int CategoryId);

/// <summary>Catalog filters: /api/items?q=dress&amp;categoryId=3&amp;maxPrice=50000</summary>
public class ItemQuery
{
    public string? Q { get; set; }
    public int? CategoryId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public ItemCondition? Condition { get; set; }
    public Guid? SellerId { get; set; }
    public string? Sort { get; set; } // "newest" (default),"price_asc", "price_desc"
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
