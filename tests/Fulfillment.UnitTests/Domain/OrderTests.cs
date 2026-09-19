using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;
using Fulfillment.UnitTests.Support;

namespace Fulfillment.UnitTests.Domain;

public class OrderTests
{
    [Fact]
    public void Place_deducts_stock_and_snapshots_the_price()
    {
        var product = TestData.Product(stock: 10, price: 25m);

        var order = Order.Place("Ada", "ada@example.com", [(product, 4)]);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(6, product.StockQuantity);
        var item = Assert.Single(order.Items);
        Assert.Equal(25m, item.UnitPrice);
        Assert.Equal(100m, order.Total);

        product.UpdateDetails(product.Name, null, 999m, product.ReorderThreshold, true);
        Assert.Equal(100m, order.Total); // later price changes must not rewrite history
    }

    [Fact]
    public void Place_with_several_lines_totals_them()
    {
        var a = TestData.Product("A", price: 10m);
        var b = TestData.Product("B", price: 2.5m);

        var order = Order.Place("Ada", "ada@example.com", [(a, 2), (b, 4)]);

        Assert.Equal(30m, order.Total);
    }

    [Fact]
    public void Place_rejects_the_whole_order_and_touches_no_stock_when_any_line_is_short()
    {
        var plenty = TestData.Product("PLENTY", stock: 100);
        var scarce = TestData.Product("SCARCE", stock: 2);

        var ex = Assert.Throws<ConflictException>(() =>
            Order.Place("Ada", "ada@example.com", [(plenty, 5), (scarce, 3)]));

        Assert.Contains("SCARCE (requested 3, available 2)", ex.Message);
        Assert.Equal(100, plenty.StockQuantity);
        Assert.Equal(2, scarce.StockQuantity);
    }

    [Fact]
    public void Place_reports_every_problem_line_not_just_the_first()
    {
        var a = TestData.Product("A", stock: 1);
        var b = TestData.Product("B", stock: 1);

        var ex = Assert.Throws<ConflictException>(() =>
            Order.Place("Ada", "ada@example.com", [(a, 5), (b, 5)]));

        Assert.Contains("A (requested 5", ex.Message);
        Assert.Contains("B (requested 5", ex.Message);
    }

    [Fact]
    public void Place_allows_ordering_exactly_the_remaining_stock()
    {
        var product = TestData.Product(stock: 3);

        Order.Place("Ada", "ada@example.com", [(product, 3)]);

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void Place_rejects_inactive_products()
    {
        var product = TestData.Product("OLD");
        product.Deactivate();

        var ex = Assert.Throws<ConflictException>(() =>
            Order.Place("Ada", "ada@example.com", [(product, 1)]));

        Assert.Contains("OLD is not available", ex.Message);
    }

    [Fact]
    public void Place_rejects_an_empty_order()
    {
        var ex = Assert.Throws<DomainValidationException>(() => Order.Place("Ada", "ada@example.com", []));

        Assert.Contains("Items", ex.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Place_rejects_non_positive_quantities(int quantity)
    {
        var product = TestData.Product();

        var ex = Assert.Throws<DomainValidationException>(() =>
            Order.Place("Ada", "ada@example.com", [(product, quantity)]));

        Assert.Contains("Items", ex.Errors.Keys);
        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public void Place_rejects_the_same_product_twice()
    {
        var product = TestData.Product();

        var ex = Assert.Throws<DomainValidationException>(() =>
            Order.Place("Ada", "ada@example.com", [(product, 1), (product, 2)]));

        Assert.Contains("Items", ex.Errors.Keys);
        Assert.Equal(10, product.StockQuantity);
    }

    [Theory]
    [InlineData("", "ada@example.com", "CustomerName")]
    [InlineData("  ", "ada@example.com", "CustomerName")]
    [InlineData("Ada", "", "CustomerEmail")]
    [InlineData("Ada", "not-an-email", "CustomerEmail")]
    [InlineData("Ada", "Ada <ada@example.com>", "CustomerEmail")]
    public void Place_validates_customer_details(string name, string email, string field)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Order.Place(name, email, [(TestData.Product(), 1)]));

        Assert.Contains(field, ex.Errors.Keys);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    public void ChangeStatus_allows_valid_transitions(OrderStatus from, OrderStatus to)
    {
        var order = TestData.OrderIn(from);

        order.ChangeStatus(to);

        Assert.Equal(to, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    public void ChangeStatus_rejects_invalid_transitions(OrderStatus from, OrderStatus to)
    {
        var order = TestData.OrderIn(from);

        var ex = Assert.Throws<ConflictException>(() => order.ChangeStatus(to));

        Assert.Contains($"from {from} to {to}", ex.Message);
        Assert.Equal(from, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    public void Cancelling_returns_the_reserved_stock(OrderStatus cancelFrom)
    {
        var product = TestData.Product(stock: 10);
        var order = TestData.OrderIn(cancelFrom, product);
        Assert.Equal(9, product.StockQuantity);

        order.ChangeStatus(OrderStatus.Cancelled);

        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public void Non_cancelling_transitions_leave_stock_alone()
    {
        var product = TestData.Product(stock: 10);
        var order = TestData.OrderIn(OrderStatus.Pending, product);

        order.ChangeStatus(OrderStatus.Confirmed);
        order.ChangeStatus(OrderStatus.Shipped);
        order.ChangeStatus(OrderStatus.Delivered);

        Assert.Equal(9, product.StockQuantity);
    }

    [Fact]
    public void A_failed_transition_does_not_return_stock()
    {
        var product = TestData.Product(stock: 10);
        var order = TestData.OrderIn(OrderStatus.Shipped, product);

        Assert.Throws<ConflictException>(() => order.ChangeStatus(OrderStatus.Cancelled));

        Assert.Equal(9, product.StockQuantity);
    }
}
