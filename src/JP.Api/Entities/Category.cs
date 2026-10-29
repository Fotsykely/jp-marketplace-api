namespace JP.Api.Entities;

/// <summary>Hierarchical category: Women > Clothing > Dresses, etc.</summary>
public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];
}
