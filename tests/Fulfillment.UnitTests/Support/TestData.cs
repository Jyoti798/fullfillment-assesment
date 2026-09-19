using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;

namespace Fulfillment.UnitTests.Support;

internal static class TestData
{
    public static Product Product(string sku = "SKU-1", int stock = 10, int threshold = 3, decimal price = 10m) =>
        new(sku, $"Product {sku}", null, price, stock, threshold);

    public static Order Order(Product product, int quantity = 1) =>
        global::Fulfillment.Domain.Entities.Order.Place("Ada Lovelace", "ada@example.com", [(product, quantity)]);

    /// <summary>Walks a fresh order through the lifecycle until it reaches <paramref name="status"/>.</summary>
    public static Order OrderIn(OrderStatus status, Product? product = null)
    {
        var order = Order(product ?? Product());

        var path = status switch
        {
            OrderStatus.Pending => Array.Empty<OrderStatus>(),
            OrderStatus.Confirmed => [OrderStatus.Confirmed],
            OrderStatus.Shipped => [OrderStatus.Confirmed, OrderStatus.Shipped],
            OrderStatus.Delivered => [OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered],
            OrderStatus.Cancelled => [OrderStatus.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        foreach (var step in path)
        {
            order.ChangeStatus(step);
        }

        return order;
    }
}
