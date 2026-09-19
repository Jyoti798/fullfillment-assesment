using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Application;

public class OrderServiceTests
{
    private readonly TestDatabase _db = new();

    private OrderService CreateService(FulfillmentDbContext context) => new(
        new OrderRepository(context),
        new ProductRepository(context),
        new UnitOfWork(context, NullLogger<UnitOfWork>.Instance),
        NullLogger<OrderService>.Instance);

    private async Task<Product> SeedProductAsync(string sku = "SKU-1", int stock = 10, decimal price = 10m)
    {
        await using var context = _db.CreateContext();
        var product = TestData.Product(sku, stock, threshold: 2, price: price);
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    private async Task<int> StockOfAsync(Guid productId)
    {
        await using var context = _db.CreateContext();
        return (await context.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;
    }

    private static CreateOrderRequest RequestFor(params (Guid ProductId, int Quantity)[] lines) => new()
    {
        CustomerName = "Ada Lovelace",
        CustomerEmail = "ada@example.com",
        Items = lines.Select(l => new OrderItemRequest { ProductId = l.ProductId, Quantity = l.Quantity }).ToList()
    };

    [Fact]
    public async Task PlaceAsync_persists_the_order_and_deducts_stock()
    {
        var product = await SeedProductAsync(stock: 10, price: 12.50m);

        await using var context = _db.CreateContext();
        var response = await CreateService(context).PlaceAsync(RequestFor((product.Id, 4)), CancellationToken.None);

        Assert.Equal(OrderStatus.Pending, response.Status);
        Assert.Equal(50m, response.Total);
        var line = Assert.Single(response.Items);
        Assert.Equal(product.Sku, line.Sku);
        Assert.Equal(6, await StockOfAsync(product.Id));

        await using var reload = _db.CreateContext();
        var stored = await CreateService(reload).GetAsync(response.Id, CancellationToken.None);
        Assert.Equal(response.Total, stored.Total);
        Assert.Equal(response.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public async Task PlaceAsync_with_insufficient_stock_persists_nothing()
    {
        var plenty = await SeedProductAsync("PLENTY", stock: 50);
        var scarce = await SeedProductAsync("SCARCE", stock: 1);

        await using var context = _db.CreateContext();
        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(context).PlaceAsync(RequestFor((plenty.Id, 5), (scarce.Id, 2)), CancellationToken.None));

        Assert.Equal(50, await StockOfAsync(plenty.Id));
        Assert.Equal(1, await StockOfAsync(scarce.Id));
        await using var check = _db.CreateContext();
        Assert.Empty(await check.Orders.ToListAsync());
    }

    [Fact]
    public async Task PlaceAsync_rejects_unknown_products_as_a_validation_error()
    {
        var unknown = Guid.NewGuid();

        await using var context = _db.CreateContext();
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            CreateService(context).PlaceAsync(RequestFor((unknown, 1)), CancellationToken.None));

        Assert.Contains(unknown.ToString(), ex.Errors["Items"].Single());
    }

    [Fact]
    public async Task PlaceAsync_rejects_a_repeated_product()
    {
        var product = await SeedProductAsync();

        await using var context = _db.CreateContext();
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            CreateService(context).PlaceAsync(RequestFor((product.Id, 1), (product.Id, 2)), CancellationToken.None));

        Assert.Equal(10, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task ChangeStatusAsync_walks_the_lifecycle_and_persists_each_step()
    {
        var product = await SeedProductAsync();
        Guid orderId;
        await using (var context = _db.CreateContext())
        {
            orderId = (await CreateService(context).PlaceAsync(RequestFor((product.Id, 2)), CancellationToken.None)).Id;
        }

        foreach (var next in new[] { OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered })
        {
            await using var context = _db.CreateContext();
            var updated = await CreateService(context)
                .ChangeStatusAsync(orderId, new UpdateOrderStatusRequest { Status = next }, CancellationToken.None);
            Assert.Equal(next, updated.Status);
        }

        await using var check = _db.CreateContext();
        Assert.Equal(OrderStatus.Delivered, (await CreateService(check).GetAsync(orderId, CancellationToken.None)).Status);
        Assert.Equal(8, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task ChangeStatusAsync_cancelling_restores_stock_in_the_database()
    {
        var product = await SeedProductAsync(stock: 10);
        Guid orderId;
        await using (var context = _db.CreateContext())
        {
            orderId = (await CreateService(context).PlaceAsync(RequestFor((product.Id, 7)), CancellationToken.None)).Id;
        }

        Assert.Equal(3, await StockOfAsync(product.Id));

        await using (var context = _db.CreateContext())
        {
            await CreateService(context).ChangeStatusAsync(
                orderId, new UpdateOrderStatusRequest { Status = OrderStatus.Cancelled }, CancellationToken.None);
        }

        Assert.Equal(10, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task ChangeStatusAsync_with_an_invalid_transition_changes_nothing()
    {
        var product = await SeedProductAsync();
        Guid orderId;
        await using (var context = _db.CreateContext())
        {
            orderId = (await CreateService(context).PlaceAsync(RequestFor((product.Id, 1)), CancellationToken.None)).Id;
        }

        await using (var context = _db.CreateContext())
        {
            await Assert.ThrowsAsync<ConflictException>(() => CreateService(context).ChangeStatusAsync(
                orderId, new UpdateOrderStatusRequest { Status = OrderStatus.Delivered }, CancellationToken.None));
        }

        await using var check = _db.CreateContext();
        Assert.Equal(OrderStatus.Pending, (await CreateService(check).GetAsync(orderId, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task PlaceAsync_survives_losing_a_race_to_another_order_when_stock_still_allows()
    {
        var product = await SeedProductAsync(stock: 10);
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).PlaceAsync(RequestFor((product.Id, 3)), CancellationToken.None);
        });

        await using var context = _db.CreateContext(race);
        var response = await CreateService(context).PlaceAsync(RequestFor((product.Id, 2)), CancellationToken.None);

        Assert.Equal(OrderStatus.Pending, response.Status);
        Assert.Equal(5, await StockOfAsync(product.Id)); // 10 - 3 (the rival) - 2 (this order)
        await using var check = _db.CreateContext();
        Assert.Equal(2, await check.Orders.CountAsync());
    }

    [Fact]
    public async Task PlaceAsync_reports_a_shortage_discovered_on_retry_as_a_genuine_conflict()
    {
        var product = await SeedProductAsync(stock: 5);
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).PlaceAsync(RequestFor((product.Id, 4)), CancellationToken.None);
        });

        await using var context = _db.CreateContext(race);
        // Exact type on purpose: this is a real "not enough stock", not a retryable concurrency conflict.
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(context).PlaceAsync(RequestFor((product.Id, 3)), CancellationToken.None));

        Assert.Contains("requested 3, available 1", ex.Message);
        Assert.Equal(1, await StockOfAsync(product.Id));
    }

    [Fact]
    public async Task ChangeStatusAsync_losing_a_cancel_race_does_not_restore_the_stock_twice()
    {
        var product = await SeedProductAsync(stock: 10);
        Guid orderId;
        await using (var context = _db.CreateContext())
        {
            orderId = (await CreateService(context).PlaceAsync(RequestFor((product.Id, 4)), CancellationToken.None)).Id;
        }

        var cancel = new UpdateOrderStatusRequest { Status = OrderStatus.Cancelled };
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).ChangeStatusAsync(orderId, cancel, CancellationToken.None);
        });

        await using var loser = _db.CreateContext(race);
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(loser).ChangeStatusAsync(orderId, cancel, CancellationToken.None));

        Assert.Contains("from Cancelled to Cancelled", ex.Message);
        Assert.Equal(10, await StockOfAsync(product.Id)); // returned once, not twice (which would be 14)
    }

    [Fact]
    public async Task GetAsync_and_ChangeStatusAsync_throw_NotFound_for_unknown_orders()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ChangeStatusAsync(
            Guid.NewGuid(), new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed }, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_filters_by_status_and_pages_newest_first()
    {
        var product = await SeedProductAsync(stock: 100);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            _db.Time.Advance(TimeSpan.FromMinutes(1));
            await using var context = _db.CreateContext();
            ids.Add((await CreateService(context).PlaceAsync(RequestFor((product.Id, 1)), CancellationToken.None)).Id);
        }

        await using (var context = _db.CreateContext())
        {
            await CreateService(context).ChangeStatusAsync(
                ids[0], new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed }, CancellationToken.None);
        }

        await using var query = _db.CreateContext();
        var service = CreateService(query);

        var confirmed = await service.ListAsync(new OrderListQuery { Status = OrderStatus.Confirmed }, CancellationToken.None);
        Assert.Equal(1, confirmed.TotalCount);
        Assert.Equal(ids[0], Assert.Single(confirmed.Items).Id);

        var firstPage = await service.ListAsync(new OrderListQuery { Page = 1, PageSize = 2 }, CancellationToken.None);
        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal([ids[4], ids[3]], firstPage.Items.Select(o => o.Id).ToArray());

        var lastPage = await service.ListAsync(new OrderListQuery { Page = 3, PageSize = 2 }, CancellationToken.None);
        Assert.Equal([ids[0]], lastPage.Items.Select(o => o.Id).ToArray());
    }
}
