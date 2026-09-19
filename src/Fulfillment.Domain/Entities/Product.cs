using Fulfillment.Domain.Common;
using Fulfillment.Domain.Exceptions;

namespace Fulfillment.Domain.Entities;

public class Product : Entity
{
    // Required by EF Core.
    private Product()
    {
        Sku = string.Empty;
        Name = string.Empty;
    }

    public Product(string sku, string name, string? description, decimal unitPrice, int stockQuantity, int reorderThreshold)
    {
        Sku = NormalizeSku(sku);
        Name = RequireName(name);
        Description = description?.Trim();
        UnitPrice = RequirePrice(unitPrice);
        StockQuantity = RequireNonNegative(stockQuantity, nameof(StockQuantity));
        ReorderThreshold = RequireNonNegative(reorderThreshold, nameof(ReorderThreshold));
        IsActive = true;
    }

    public string Sku { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int StockQuantity { get; private set; }

    /// <summary>Stock at or below this level is considered low and triggers an alert.</summary>
    public int ReorderThreshold { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsLowStock => StockQuantity <= ReorderThreshold;

    public void UpdateDetails(string name, string? description, decimal unitPrice, int reorderThreshold, bool isActive)
    {
        Name = RequireName(name);
        Description = description?.Trim();
        UnitPrice = RequirePrice(unitPrice);
        ReorderThreshold = RequireNonNegative(reorderThreshold, nameof(ReorderThreshold));
        IsActive = isActive;
    }

    /// <summary>Adds (positive) or removes (negative) stock. Stock can never drop below zero.</summary>
    public void AdjustStock(int delta)
    {
        var updated = (long)StockQuantity + delta;
        if (updated < 0)
        {
            throw new ConflictException(
                $"Product '{Sku}' has {StockQuantity} in stock; cannot remove {-(long)delta}.");
        }

        if (updated > int.MaxValue)
        {
            throw new DomainValidationException(nameof(delta), "Resulting stock quantity is too large.");
        }

        StockQuantity = (int)updated;
    }

    public void Deactivate() => IsActive = false;

    public static string NormalizeSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainValidationException(nameof(Sku), "SKU is required.");
        }

        return sku.Trim().ToUpperInvariant();
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException(nameof(Name), "Name is required.");
        }

        return name.Trim();
    }

    private static decimal RequirePrice(decimal price)
    {
        if (price < 0)
        {
            throw new DomainValidationException(nameof(UnitPrice), "Unit price cannot be negative.");
        }

        // The column is decimal(18,2), but SQLite stores decimals as text and enforces no precision. Reject
        // sub-cent prices here instead of silently storing them and letting order totals drift off whole cents.
        if (decimal.Round(price, 2) != price)
        {
            throw new DomainValidationException(nameof(UnitPrice), "Unit price cannot have more than two decimal places.");
        }

        return price;
    }

    private static int RequireNonNegative(int value, string field)
    {
        if (value < 0)
        {
            throw new DomainValidationException(field, $"{field} cannot be negative.");
        }

        return value;
    }
}
