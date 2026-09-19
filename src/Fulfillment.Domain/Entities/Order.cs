using System.Net.Mail;
using Fulfillment.Domain.Common;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;

namespace Fulfillment.Domain.Entities;

public class Order : Entity
{
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
            [OrderStatus.Confirmed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
            [OrderStatus.Shipped] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = []
        };

    private readonly List<OrderItem> _items = [];

    // Required by EF Core.
    private Order()
    {
        CustomerName = string.Empty;
        CustomerEmail = string.Empty;
    }

    public string CustomerName { get; private set; }

    public string CustomerEmail { get; private set; }

    public OrderStatus Status { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;

    public decimal Total => _items.Sum(i => i.LineTotal);

    /// <summary>
    /// Places a new order and deducts the ordered quantities from stock. Either every line can be
    /// fulfilled or the whole order is rejected and no stock is touched.
    /// </summary>
    public static Order Place(string customerName, string customerEmail, IReadOnlyCollection<(Product Product, int Quantity)> lines)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(customerName))
        {
            errors[nameof(CustomerName)] = ["Customer name is required."];
        }

        if (!IsValidEmail(customerEmail))
        {
            errors[nameof(CustomerEmail)] = ["A valid customer email is required."];
        }

        if (lines.Count == 0)
        {
            errors["Items"] = ["An order must contain at least one item."];
        }
        else if (lines.Any(l => l.Quantity <= 0))
        {
            errors["Items"] = ["Every item quantity must be greater than zero."];
        }
        else if (lines.GroupBy(l => l.Product.Id).Any(g => g.Count() > 1))
        {
            errors["Items"] = ["Each product may appear only once per order."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        var problems = new List<string>();
        foreach (var (product, quantity) in lines)
        {
            if (!product.IsActive)
            {
                problems.Add($"{product.Sku} is not available");
            }
            else if (product.StockQuantity < quantity)
            {
                problems.Add($"{product.Sku} (requested {quantity}, available {product.StockQuantity})");
            }
        }

        if (problems.Count > 0)
        {
            throw new ConflictException($"Order cannot be fulfilled: {string.Join("; ", problems)}.");
        }

        var order = new Order
        {
            CustomerName = customerName.Trim(),
            CustomerEmail = customerEmail.Trim(),
            Status = OrderStatus.Pending
        };

        foreach (var (product, quantity) in lines)
        {
            product.AdjustStock(-quantity);
            order._items.Add(new OrderItem(product, quantity));
        }

        return order;
    }

    public bool CanTransitionTo(OrderStatus next) => AllowedTransitions[Status].Contains(next);

    /// <summary>Moves the order to <paramref name="next"/>. Cancelling returns the reserved stock.</summary>
    public void ChangeStatus(OrderStatus next)
    {
        if (!CanTransitionTo(next))
        {
            throw new ConflictException($"Cannot change order status from {Status} to {next}.");
        }

        if (next == OrderStatus.Cancelled)
        {
            foreach (var item in _items)
            {
                item.Product.AdjustStock(item.Quantity);
            }
        }

        Status = next;
    }

    private static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        return MailAddress.TryCreate(email.Trim(), out var address) && address.Address == email.Trim();
    }
}
