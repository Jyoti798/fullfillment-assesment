using Fulfillment.Domain.Common;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;

namespace Fulfillment.Domain.Entities;

public class Alert : Entity
{
    // Required by EF Core.
    private Alert()
    {
        Message = string.Empty;
        Product = null!;
    }

    public Guid ProductId { get; private set; }

    public Product Product { get; private set; }

    public AlertType Type { get; private set; }

    public AlertStatus Status { get; private set; }

    public int StockAtDetection { get; private set; }

    public int Threshold { get; private set; }

    public string Message { get; private set; }

    public DateTime? AcknowledgedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public bool IsUnresolved => Status != AlertStatus.Resolved;

    public static Alert RaiseLowStock(Product product) => new()
    {
        ProductId = product.Id,
        Product = product,
        Type = AlertType.LowStock,
        Status = AlertStatus.Open,
        StockAtDetection = product.StockQuantity,
        Threshold = product.ReorderThreshold,
        Message = $"Low stock for {product.Sku} ({product.Name}): {product.StockQuantity} on hand, " +
                  $"reorder threshold is {product.ReorderThreshold}."
    };

    public void Acknowledge(DateTime utcNow)
    {
        if (Status != AlertStatus.Open)
        {
            throw new ConflictException($"Only open alerts can be acknowledged; this alert is {Status}.");
        }

        Status = AlertStatus.Acknowledged;
        AcknowledgedAt = utcNow;
    }

    public void Resolve(DateTime utcNow)
    {
        if (Status == AlertStatus.Resolved)
        {
            throw new ConflictException("This alert is already resolved.");
        }

        Status = AlertStatus.Resolved;
        ResolvedAt = utcNow;
    }
}
