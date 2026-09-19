using System.Net;
using System.Net.Http.Json;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.IntegrationTests;

public class OrdersApiTests(FulfillmentApiFactory factory) : IClassFixture<FulfillmentApiFactory>
{
    private HttpClient _client => factory.Client;

    [Fact]
    public async Task Placing_an_order_returns_201_deducts_stock_and_totals_the_lines()
    {
        var product = await _client.CreateProductAsync(stock: 10, price: 12.5m);

        var response = await _client.PlaceOrderAsync(product.Id, 4);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.ReadAsync<OrderResponse>();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(50m, order.Total);
        Assert.Equal(product.Sku, Assert.Single(order.Items).Sku);
        Assert.EndsWith($"/api/orders/{order.Id}", response.Headers.Location!.ToString());
        Assert.Equal(6, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task The_status_is_serialised_as_a_readable_string()
    {
        var product = await _client.CreateProductAsync();
        var response = await _client.PlaceOrderAsync(product.Id, 1);

        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"status\":\"Pending\"", json);
    }

    [Fact]
    public async Task An_order_can_be_walked_through_the_whole_lifecycle()
    {
        var product = await _client.CreateProductAsync(stock: 10);
        var order = await (await _client.PlaceOrderAsync(product.Id, 2)).ReadAsync<OrderResponse>();

        foreach (var next in new[] { OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered })
        {
            var response = await _client.SetStatusAsync(order.Id, next);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(next, (await response.ReadAsync<OrderResponse>()).Status);
        }

        var stored = await (await _client.GetAsync($"/api/orders/{order.Id}")).ReadAsync<OrderResponse>();
        Assert.Equal(OrderStatus.Delivered, stored.Status);
        Assert.Equal(8, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Ordering_more_than_is_in_stock_returns_409_and_changes_nothing()
    {
        var product = await _client.CreateProductAsync(stock: 3);

        var response = await _client.PlaceOrderAsync(product.Id, 4);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Contains(product.Sku, problem.Detail);
        Assert.Contains("requested 4, available 3", problem.Detail);
        Assert.Equal(3, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task One_short_line_rejects_the_whole_multi_line_order()
    {
        var plenty = await _client.CreateProductAsync(stock: 100);
        var scarce = await _client.CreateProductAsync(stock: 1);

        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Ada",
            customerEmail = "ada@example.com",
            items = new[] { new { productId = plenty.Id, quantity = 5 }, new { productId = scarce.Id, quantity = 2 } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(100, await _client.StockOfAsync(plenty.Id));
        Assert.Equal(1, await _client.StockOfAsync(scarce.Id));
    }

    [Fact]
    public async Task Cancelling_returns_the_stock()
    {
        var product = await _client.CreateProductAsync(stock: 10);
        var order = await (await _client.PlaceOrderAsync(product.Id, 7)).ReadAsync<OrderResponse>();
        Assert.Equal(3, await _client.StockOfAsync(product.Id));

        var cancel = await _client.SetStatusAsync(order.Id, OrderStatus.Cancelled);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(10, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Cancelling_twice_does_not_return_the_stock_twice()
    {
        var product = await _client.CreateProductAsync(stock: 10);
        var order = await (await _client.PlaceOrderAsync(product.Id, 7)).ReadAsync<OrderResponse>();
        await _client.SetStatusAsync(order.Id, OrderStatus.Cancelled);

        var second = await _client.SetStatusAsync(order.Id, OrderStatus.Cancelled);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(10, await _client.StockOfAsync(product.Id));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending)]
    public async Task Invalid_status_transitions_from_pending_return_409(OrderStatus target)
    {
        var product = await _client.CreateProductAsync();
        var order = await (await _client.PlaceOrderAsync(product.Id, 1)).ReadAsync<OrderResponse>();

        var response = await _client.SetStatusAsync(order.Id, target);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Contains($"from Pending to {target}", problem.Detail);
    }

    [Fact]
    public async Task A_shipped_order_can_no_longer_be_cancelled()
    {
        var product = await _client.CreateProductAsync(stock: 10);
        var order = await (await _client.PlaceOrderAsync(product.Id, 2)).ReadAsync<OrderResponse>();
        await _client.SetStatusAsync(order.Id, OrderStatus.Confirmed);
        await _client.SetStatusAsync(order.Id, OrderStatus.Shipped);

        var response = await _client.SetStatusAsync(order.Id, OrderStatus.Cancelled);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(8, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Unknown_status_values_are_rejected_with_400()
    {
        var product = await _client.CreateProductAsync();
        var order = await (await _client.PlaceOrderAsync(product.Id, 1)).ReadAsync<OrderResponse>();

        var named = await _client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = "Bogus" });
        var numeric = await _client.PutAsJsonAsync($"/api/orders/{order.Id}/status", new { status = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, named.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);
    }

    [Fact]
    public async Task Invalid_order_bodies_return_400_with_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "",
            customerEmail = "not-an-email",
            items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Contains("CustomerName", problem.Errors.Keys);
        Assert.Contains("CustomerEmail", problem.Errors.Keys);
        Assert.Contains("Items", problem.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Non_positive_quantities_return_400(int quantity)
    {
        var product = await _client.CreateProductAsync();

        var response = await _client.PlaceOrderAsync(product.Id, quantity);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(10, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Unknown_products_return_400_and_repeated_products_return_400()
    {
        var product = await _client.CreateProductAsync(stock: 10);

        var unknown = await _client.PlaceOrderAsync(Guid.NewGuid(), 1);
        var repeated = await _client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Ada",
            customerEmail = "ada@example.com",
            items = new[] { new { productId = product.Id, quantity = 1 }, new { productId = product.Id, quantity = 2 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        Assert.Equal(10, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Deactivated_products_cannot_be_ordered_but_old_orders_survive()
    {
        var product = await _client.CreateProductAsync(stock: 10);
        var order = await (await _client.PlaceOrderAsync(product.Id, 1)).ReadAsync<OrderResponse>();
        await _client.DeleteAsync($"/api/products/{product.Id}");

        var again = await _client.PlaceOrderAsync(product.Id, 1);
        var old = await _client.GetAsync($"/api/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.OK, old.StatusCode);
    }

    [Fact]
    public async Task Unknown_orders_return_404()
    {
        var get = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");
        var put = await _client.SetStatusAsync(Guid.NewGuid(), OrderStatus.Confirmed);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task A_price_change_after_ordering_does_not_alter_the_order()
    {
        var product = await _client.CreateProductAsync(stock: 10, price: 10m);
        var order = await (await _client.PlaceOrderAsync(product.Id, 2)).ReadAsync<OrderResponse>();

        await _client.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            name = "Pricier",
            unitPrice = 500m,
            reorderThreshold = 2,
            isActive = true
        });

        var stored = await (await _client.GetAsync($"/api/orders/{order.Id}")).ReadAsync<OrderResponse>();
        Assert.Equal(20m, stored.Total);
    }

    [Fact]
    public async Task List_filters_by_status_and_pages()
    {
        var product = await _client.CreateProductAsync(stock: 100);
        var placed = new List<OrderResponse>();
        for (var i = 0; i < 3; i++)
        {
            placed.Add(await (await _client.PlaceOrderAsync(product.Id, 1)).ReadAsync<OrderResponse>());
        }

        await _client.SetStatusAsync(placed[0].Id, OrderStatus.Cancelled);

        var cancelled = await (await _client.GetAsync("/api/orders?status=Cancelled&pageSize=100"))
            .ReadAsync<PagedResult<OrderResponse>>();
        Assert.Contains(cancelled.Items, o => o.Id == placed[0].Id);
        Assert.All(cancelled.Items, o => Assert.Equal(OrderStatus.Cancelled, o.Status));

        var page = await (await _client.GetAsync("/api/orders?page=1&pageSize=2")).ReadAsync<PagedResult<OrderResponse>>();
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.TotalCount >= 3);

        var badStatus = await _client.GetAsync("/api/orders?status=Bogus");
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

    [Fact]
    public async Task Concurrent_orders_for_a_well_stocked_product_all_succeed()
    {
        // Stock is plentiful, so no request should fail just because another one committed a moment earlier.
        // A request only loses a race when someone else commits, so with 5 attempts and 5 buyers all must win.
        const int buyers = 5;
        var product = await _client.CreateProductAsync(stock: 100);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, buyers).Select(_ => _client.PlaceOrderAsync(product.Id, 2)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(100 - buyers * 2, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task Concurrent_orders_for_the_last_units_never_oversell()
    {
        const int stock = 5;
        const int buyers = 15;
        var product = await _client.CreateProductAsync(stock: stock);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, buyers).Select(_ => _client.PlaceOrderAsync(product.Id, 1)));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflicts = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        var unexpected = responses.Where(r => r.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.Conflict)).ToList();

        Assert.Empty(unexpected); // never a 500 under contention
        Assert.Equal(buyers, created + conflicts);
        Assert.InRange(created, 1, stock);
        Assert.Equal(stock - created, await _client.StockOfAsync(product.Id));
    }
}
