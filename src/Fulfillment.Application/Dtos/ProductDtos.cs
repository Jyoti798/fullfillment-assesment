using System.ComponentModel.DataAnnotations;

namespace Fulfillment.Application.Dtos;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal UnitPrice,
    int StockQuantity,
    int ReorderThreshold,
    bool IsActive,
    bool IsLowStock,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class CreateProductRequest
{
    [Required, StringLength(64)]
    public string Sku { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal UnitPrice { get; init; }

    [Range(0, int.MaxValue)]
    public int StockQuantity { get; init; }

    [Range(0, int.MaxValue)]
    public int ReorderThreshold { get; init; }
}

public sealed class UpdateProductRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; init; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal UnitPrice { get; init; }

    [Range(0, int.MaxValue)]
    public int ReorderThreshold { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class AdjustStockRequest
{
    /// <summary>Positive to add stock (e.g. a delivery), negative to remove it (e.g. shrinkage).</summary>
    [Range(-1_000_000, 1_000_000)]
    public int Delta { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }
}

public sealed class ProductListQuery : PagedQuery
{
    [StringLength(100)]
    public string? Search { get; init; }

    public bool LowStockOnly { get; init; }
}
