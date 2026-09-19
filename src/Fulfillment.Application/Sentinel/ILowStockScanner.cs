namespace Fulfillment.Application.Sentinel;

public sealed record LowStockScanResult(int AlertsCreated, int AlertsResolved);

public interface ILowStockScanner
{
    /// <summary>
    /// Reconciles alerts with current stock levels: raises an alert for each active product that is at or
    /// below its reorder threshold, and resolves alerts whose product has recovered or been deactivated.
    /// Safe to run repeatedly; it never creates a second unresolved alert for the same product.
    /// </summary>
    Task<LowStockScanResult> ScanAsync(CancellationToken cancellationToken);
}
