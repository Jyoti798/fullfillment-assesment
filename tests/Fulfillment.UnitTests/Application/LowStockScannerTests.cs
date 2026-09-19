using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Fulfillment.Application.Sentinel;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Application;

public class LowStockScannerTests
{
    private readonly TestDatabase _db = new();

    // A fresh context per scan mirrors the sentinel, which opens a new DI scope for every cycle.
    private async Task<LowStockScanResult> ScanAsync()
    {
        await using var context = _db.CreateContext();
        var scanner = new LowStockScanner(
            new ProductRepository(context),
            new AlertRepository(context),
            new UnitOfWork(context, NullLogger<UnitOfWork>.Instance),
            _db.Time,
            NullLogger<LowStockScanner>.Instance);

        return await scanner.ScanAsync(CancellationToken.None);
    }

    private async Task SeedAsync(params Product[] products)
    {
        await using var context = _db.CreateContext();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private async Task ChangeProductAsync(string sku, Action<Product> change)
    {
        await using var context = _db.CreateContext();
        change(await context.Products.SingleAsync(p => p.Sku == sku));
        await context.SaveChangesAsync();
    }

    private async Task<List<Alert>> AllAlertsAsync()
    {
        await using var context = _db.CreateContext();
        return await context.Alerts.AsNoTracking().Include(a => a.Product).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Scan_raises_alerts_only_for_active_products_at_or_below_threshold()
    {
        var inactiveLow = TestData.Product("INACTIVE", stock: 0, threshold: 5);
        inactiveLow.Deactivate();
        await SeedAsync(
            TestData.Product("BELOW", stock: 2, threshold: 5),
            TestData.Product("AT", stock: 5, threshold: 5),
            TestData.Product("HEALTHY", stock: 6, threshold: 5),
            inactiveLow);

        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(2, 0), result);
        var alerts = await AllAlertsAsync();
        Assert.Equal(["AT", "BELOW"], alerts.Select(a => a.Product.Sku).Order().ToArray());
        Assert.All(alerts, a => Assert.Equal(AlertStatus.Open, a.Status));
    }

    [Fact]
    public async Task Scan_is_idempotent()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));

        var first = await ScanAsync();
        var second = await ScanAsync();
        var third = await ScanAsync();

        Assert.Equal(1, first.AlertsCreated);
        Assert.Equal(new LowStockScanResult(0, 0), second);
        Assert.Equal(new LowStockScanResult(0, 0), third);
        Assert.Single(await AllAlertsAsync());
    }

    [Fact]
    public async Task Scan_does_not_raise_a_second_alert_while_one_is_acknowledged()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));
        await ScanAsync();

        await using (var context = _db.CreateContext())
        {
            var alert = await context.Alerts.SingleAsync();
            alert.Acknowledge(_db.Time.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync();
        }

        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(0, 0), result);
        Assert.Single(await AllAlertsAsync());
    }

    [Fact]
    public async Task Scan_resolves_the_alert_once_stock_recovers()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));
        await ScanAsync();

        _db.Time.Advance(TimeSpan.FromMinutes(30));
        await ChangeProductAsync("LOW", p => p.AdjustStock(20));
        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(0, 1), result);
        var alert = Assert.Single(await AllAlertsAsync());
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Equal(_db.Time.GetUtcNow().UtcDateTime, alert.ResolvedAt);
        Assert.Equal(DateTimeKind.Utc, alert.ResolvedAt!.Value.Kind);
    }

    [Fact]
    public async Task Scan_resolves_an_acknowledged_alert_when_stock_recovers()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));
        await ScanAsync();
        await using (var context = _db.CreateContext())
        {
            (await context.Alerts.SingleAsync()).Acknowledge(_db.Time.GetUtcNow().UtcDateTime);
            await context.SaveChangesAsync();
        }

        await ChangeProductAsync("LOW", p => p.AdjustStock(20));
        var result = await ScanAsync();

        Assert.Equal(1, result.AlertsResolved);
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await AllAlertsAsync()).Status);
    }

    [Fact]
    public async Task Scan_resolves_the_alert_when_the_product_is_deactivated()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));
        await ScanAsync();

        await ChangeProductAsync("LOW", p => p.Deactivate());
        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(0, 1), result);
        Assert.Equal(AlertStatus.Resolved, Assert.Single(await AllAlertsAsync()).Status);
    }

    [Fact]
    public async Task Scan_raises_a_fresh_alert_when_a_recovered_product_runs_low_again()
    {
        await SeedAsync(TestData.Product("LOW", stock: 1, threshold: 5));
        await ScanAsync();
        _db.Time.Advance(TimeSpan.FromMinutes(1));
        await ChangeProductAsync("LOW", p => p.AdjustStock(20));
        await ScanAsync();

        _db.Time.Advance(TimeSpan.FromMinutes(1));
        await ChangeProductAsync("LOW", p => p.AdjustStock(-19));
        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(1, 0), result);
        var alerts = await AllAlertsAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal([AlertStatus.Resolved, AlertStatus.Open], alerts.Select(a => a.Status).ToArray());
    }

    [Fact]
    public async Task Scan_with_no_low_stock_does_nothing()
    {
        await SeedAsync(TestData.Product("OK", stock: 50, threshold: 5));

        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(0, 0), result);
        Assert.Empty(await AllAlertsAsync());
    }

    [Fact]
    public async Task Scan_handles_creating_and_resolving_in_the_same_pass()
    {
        await SeedAsync(
            TestData.Product("RECOVERED", stock: 1, threshold: 5),
            TestData.Product("HEALTHY", stock: 50, threshold: 5));
        await ScanAsync();

        await ChangeProductAsync("RECOVERED", p => p.AdjustStock(20));
        await ChangeProductAsync("HEALTHY", p => p.AdjustStock(-48));
        var result = await ScanAsync();

        Assert.Equal(new LowStockScanResult(1, 1), result);
    }
}
