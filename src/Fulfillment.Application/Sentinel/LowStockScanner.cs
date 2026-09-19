using Fulfillment.Application.Abstractions;
using Fulfillment.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Application.Sentinel;

internal sealed class LowStockScanner(
    IProductRepository products,
    IAlertRepository alerts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<LowStockScanner> logger) : ILowStockScanner
{
    public async Task<LowStockScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var unresolved = await alerts.GetUnresolvedAsync(cancellationToken);
        var lowStock = await products.GetLowStockAsync(cancellationToken);

        var alreadyAlerted = unresolved.Select(a => a.ProductId).ToHashSet();

        var created = 0;
        foreach (var product in lowStock.Where(p => !alreadyAlerted.Contains(p.Id)))
        {
            alerts.Add(Alert.RaiseLowStock(product));
            created++;
            logger.LogWarning(
                "Low stock: {Sku} has {StockQuantity} on hand (threshold {Threshold})",
                product.Sku, product.StockQuantity, product.ReorderThreshold);
        }

        var resolved = 0;
        foreach (var alert in unresolved.Where(a => !a.Product.IsActive || !a.Product.IsLowStock))
        {
            alert.Resolve(now);
            resolved++;
            logger.LogInformation(
                "Low stock alert for {Sku} resolved (stock now {StockQuantity})",
                alert.Product.Sku, alert.Product.StockQuantity);
        }

        if (created > 0 || resolved > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new LowStockScanResult(created, resolved);
    }
}
