using Fulfillment.Domain.Common;

namespace Fulfillment.Domain.Entities;

public class OrderItem : Entity
{
    // Required by EF Core.
    private OrderItem()
    {
        Product = null!;
    }

    internal OrderItem(Product product, int quantity)
    {
        Product = product;
        ProductId = product.Id;
        Quantity = quantity;
        UnitPrice = product.UnitPrice; // price snapshot: later price changes must not alter existing orders
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Product Product { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal LineTotal => UnitPrice * Quantity;
}
