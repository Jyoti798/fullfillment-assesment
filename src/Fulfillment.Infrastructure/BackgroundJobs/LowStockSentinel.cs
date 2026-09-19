using Fulfillment.Application.Sentinel;
using Fulfillment.Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fulfillment.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodically asks <see cref="ILowStockScanner"/> to reconcile alerts with stock levels. This class only
/// owns scheduling, scoping and failure isolation; the business logic lives in the Application layer.
/// </summary>
internal sealed class LowStockSentinel(
    IServiceScopeFactory scopeFactory,
    IOptions<LowStockSentinelOptions> options,
    ILogger<LowStockSentinel> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(options.Value.IntervalSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Don't hold up host startup while the first scan runs.
        await Task.Yield();

        logger.LogInformation("Low Stock Sentinel started; scanning every {Interval}", _interval);

        await RunScanAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunScanAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Low Stock Sentinel stopped");
    }

    private async Task RunScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The DbContext is scoped, so every scan gets its own scope (and therefore its own DbContext).
            await using var scope = scopeFactory.CreateAsyncScope();
            var scanner = scope.ServiceProvider.GetRequiredService<ILowStockScanner>();

            var result = await scanner.ScanAsync(cancellationToken);

            logger.LogInformation(
                "Low stock scan complete: {Created} alert(s) created, {Resolved} resolved",
                result.AlertsCreated, result.AlertsResolved);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-scan.
        }
        catch (ConflictException ex)
        {
            // Expected when another instance raised or resolved the same alert first (the unique index rejects
            // the duplicate). Nothing was half-saved, and the next scan sees the other instance's result.
            logger.LogWarning("Low stock scan lost a race with another writer ({Reason}); retrying in {Interval}", ex.Message, _interval);
        }
        catch (Exception ex)
        {
            // A failed scan must never take the host down; the next tick retries.
            logger.LogError(ex, "Low stock scan failed; retrying in {Interval}", _interval);
        }
    }
}
