using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;
using Fulfillment.UnitTests.Support;

namespace Fulfillment.UnitTests.Domain;

public class AlertTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RaiseLowStock_captures_the_stock_level_at_detection()
    {
        var product = TestData.Product("WC-500", stock: 2, threshold: 5);

        var alert = Alert.RaiseLowStock(product);

        Assert.Equal(AlertStatus.Open, alert.Status);
        Assert.Equal(AlertType.LowStock, alert.Type);
        Assert.Equal(product.Id, alert.ProductId);
        Assert.Equal(2, alert.StockAtDetection);
        Assert.Equal(5, alert.Threshold);
        Assert.Contains("WC-500", alert.Message);
        Assert.True(alert.IsUnresolved);
    }

    [Fact]
    public void Snapshot_is_unaffected_by_later_stock_changes()
    {
        var product = TestData.Product(stock: 2, threshold: 5);
        var alert = Alert.RaiseLowStock(product);

        product.AdjustStock(50);

        Assert.Equal(2, alert.StockAtDetection);
    }

    [Fact]
    public void Acknowledge_moves_an_open_alert_and_records_when()
    {
        var alert = Alert.RaiseLowStock(TestData.Product(stock: 1, threshold: 5));

        alert.Acknowledge(Now);

        Assert.Equal(AlertStatus.Acknowledged, alert.Status);
        Assert.Equal(Now, alert.AcknowledgedAt);
        Assert.True(alert.IsUnresolved);
    }

    [Fact]
    public void Acknowledge_twice_is_a_conflict()
    {
        var alert = Alert.RaiseLowStock(TestData.Product(stock: 1, threshold: 5));
        alert.Acknowledge(Now);

        Assert.Throws<ConflictException>(() => alert.Acknowledge(Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolve_works_from_open_or_acknowledged(bool acknowledgeFirst)
    {
        var alert = Alert.RaiseLowStock(TestData.Product(stock: 1, threshold: 5));
        if (acknowledgeFirst)
        {
            alert.Acknowledge(Now);
        }

        alert.Resolve(Now.AddHours(1));

        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.Equal(Now.AddHours(1), alert.ResolvedAt);
        Assert.False(alert.IsUnresolved);
    }

    [Fact]
    public void A_resolved_alert_cannot_be_resolved_or_acknowledged_again()
    {
        var alert = Alert.RaiseLowStock(TestData.Product(stock: 1, threshold: 5));
        alert.Resolve(Now);

        Assert.Throws<ConflictException>(() => alert.Resolve(Now));
        Assert.Throws<ConflictException>(() => alert.Acknowledge(Now));
    }
}
