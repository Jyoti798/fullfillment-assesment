using System.Net;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Sentinel;
using Fulfillment.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fulfillment.IntegrationTests;

public class AlertsApiTests(FulfillmentApiFactory factory) : IClassFixture<FulfillmentApiFactory>
{
    private HttpClient _client => factory.Client;

    // Runs one sentinel cycle exactly as the background service would: in a fresh DI scope.
    private async Task<LowStockScanResult> RunSentinelOnceAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ILowStockScanner>().ScanAsync(CancellationToken.None);
    }

    [Fact]
    public void The_sentinel_service_is_not_registered_when_disabled_by_configuration()
    {
        var hosted = factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hosted, s => s.GetType().Name == "LowStockSentinel");
    }

    [Fact]
    public async Task A_low_stock_product_gets_exactly_one_open_alert()
    {
        var product = await _client.CreateProductAsync(stock: 2, threshold: 5);

        await RunSentinelOnceAsync();
        await RunSentinelOnceAsync();

        var alerts = await _client.AlertsForAsync(product.Id);
        var alert = Assert.Single(alerts.Items);
        Assert.Equal(AlertStatus.Open, alert.Status);
        Assert.Equal(AlertType.LowStock, alert.Type);
        Assert.Equal(product.Sku, alert.ProductSku);
        Assert.Equal(2, alert.StockAtDetection);
        Assert.Equal(5, alert.Threshold);
    }

    [Fact]
    public async Task A_healthy_product_gets_no_alert()
    {
        var product = await _client.CreateProductAsync(stock: 50, threshold: 5);

        await RunSentinelOnceAsync();

        Assert.Empty((await _client.AlertsForAsync(product.Id)).Items);
    }

    [Fact]
    public async Task An_order_that_drains_stock_below_the_threshold_triggers_an_alert()
    {
        var product = await _client.CreateProductAsync(stock: 10, threshold: 5);
        await RunSentinelOnceAsync();
        Assert.Empty((await _client.AlertsForAsync(product.Id)).Items);

        await _client.PlaceOrderAsync(product.Id, 6);
        await RunSentinelOnceAsync();

        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
        Assert.Equal(4, alert.StockAtDetection);
    }

    [Fact]
    public async Task Restocking_resolves_the_alert_and_running_low_again_raises_a_new_one()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();

        await _client.AdjustStockAsync(product.Id, 20);
        var resolve = await RunSentinelOnceAsync();
        Assert.True(resolve.AlertsResolved >= 1);
        var afterRestock = await _client.AlertsForAsync(product.Id);
        var resolved = Assert.Single(afterRestock.Items);
        Assert.Equal(AlertStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);

        await _client.AdjustStockAsync(product.Id, -19);
        await RunSentinelOnceAsync();

        var all = (await _client.AlertsForAsync(product.Id)).Items;
        Assert.Equal(2, all.Count);
        Assert.Single(all, a => a.Status == AlertStatus.Open);
        Assert.Single(all, a => a.Status == AlertStatus.Resolved);
    }

    [Fact]
    public async Task Acknowledging_marks_the_alert_and_a_second_acknowledge_conflicts()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);

        var first = await _client.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null);
        var second = await _client.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var acknowledged = await first.ReadAsync<AlertResponse>();
        Assert.Equal(AlertStatus.Acknowledged, acknowledged.Status);
        Assert.NotNull(acknowledged.AcknowledgedAt);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Acknowledged is still unresolved: the sentinel must not raise a duplicate.
        await RunSentinelOnceAsync();
        Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
    }

    [Fact]
    public async Task Alerts_can_be_fetched_by_id_and_filtered_by_status()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);

        var byId = await (await _client.GetAsync($"/api/alerts/{alert.Id}")).ReadAsync<AlertResponse>();
        var open = await (await _client.GetAsync($"/api/alerts?status=Open&productId={product.Id}"))
            .ReadAsync<PagedResult<AlertResponse>>();
        var resolved = await (await _client.GetAsync($"/api/alerts?status=Resolved&productId={product.Id}"))
            .ReadAsync<PagedResult<AlertResponse>>();

        Assert.Equal(alert.Id, byId.Id);
        Assert.Single(open.Items);
        Assert.Empty(resolved.Items);
    }

    [Fact]
    public async Task Current_stock_on_an_alert_reflects_the_live_level()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        await _client.AdjustStockAsync(product.Id, 2);

        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);

        Assert.Equal(1, alert.StockAtDetection);
        Assert.Equal(3, alert.CurrentStock);
    }

    [Fact]
    public async Task Resolving_an_open_alert_closes_it()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);

        var response = await _client.ResolveAlertAsync(alert.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.ReadAsync<AlertResponse>();
        Assert.Equal(AlertStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);
        var stored = await (await _client.GetAsync($"/api/alerts/{alert.Id}")).ReadAsync<AlertResponse>();
        Assert.Equal(AlertStatus.Resolved, stored.Status);
    }

    [Fact]
    public async Task An_acknowledged_alert_can_be_resolved()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
        await _client.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null);

        var response = await _client.ResolveAlertAsync(alert.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AlertStatus.Resolved, (await response.ReadAsync<AlertResponse>()).Status);
    }

    [Fact]
    public async Task Resolving_twice_returns_409()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
        await _client.ResolveAlertAsync(alert.Id);

        var second = await _client.ResolveAlertAsync(alert.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already resolved", (await second.ReadAsync<ProblemDetails>()).Detail);
    }

    [Fact]
    public async Task Resolving_while_stock_is_still_low_is_allowed_and_the_sentinel_raises_a_fresh_alert()
    {
        // The chosen rule: a manual resolve always works, but the alerts mirror reality, so if the product is still
        // at or below its threshold the next scan raises a new one.
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var first = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);

        var resolve = await _client.ResolveAlertAsync(first.Id);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        await RunSentinelOnceAsync();

        var alerts = (await _client.AlertsForAsync(product.Id)).Items;
        Assert.Equal(2, alerts.Count);
        var fresh = Assert.Single(alerts, a => a.Status == AlertStatus.Open);
        Assert.NotEqual(first.Id, fresh.Id);
        Assert.Single(alerts, a => a.Id == first.Id && a.Status == AlertStatus.Resolved);
    }

    [Fact]
    public async Task An_alert_the_sentinel_already_resolved_cannot_be_resolved_again()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
        await _client.AdjustStockAsync(product.Id, 20);
        await RunSentinelOnceAsync(); // auto-resolves

        var response = await _client.ResolveAlertAsync(alert.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task A_resolved_alert_cannot_be_acknowledged()
    {
        var product = await _client.CreateProductAsync(stock: 1, threshold: 5);
        await RunSentinelOnceAsync();
        var alert = Assert.Single((await _client.AlertsForAsync(product.Id)).Items);
        await _client.ResolveAlertAsync(alert.Id);

        var response = await _client.PostAsync($"/api/alerts/{alert.Id}/acknowledge", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Resolving_an_unknown_alert_returns_404()
    {
        var response = await _client.ResolveAlertAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_alerts_return_404()
    {
        var get = await _client.GetAsync($"/api/alerts/{Guid.NewGuid()}");
        var ack = await _client.PostAsync($"/api/alerts/{Guid.NewGuid()}/acknowledge", null);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ack.StatusCode);
        Assert.Equal(404, (await get.ReadAsync<ProblemDetails>()).Status);
    }
}
