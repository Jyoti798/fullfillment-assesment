using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Infrastructure;

public class PersistenceTests
{
    private readonly TestDatabase _db = new();

    private static UnitOfWork UnitOfWorkFor(FulfillmentDbContext context) =>
        new(context, NullLogger<UnitOfWork>.Instance);

    [Fact]
    public async Task Concurrent_modification_of_the_same_product_is_reported_as_a_conflict()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Products.Add(TestData.Product("RACE", stock: 1));
            await seed.SaveChangesAsync();
        }

        await using var slow = _db.CreateContext();
        await using var fast = _db.CreateContext();

        // Both requests read the product while one unit is left.
        var slowView = await slow.Products.SingleAsync();
        var fastView = await fast.Products.SingleAsync();

        // The fast request wins and takes the last unit.
        fast.Orders.Add(TestData.Order(fastView));
        await UnitOfWorkFor(fast).SaveChangesAsync(CancellationToken.None);

        // The slow request still believes stock is 1, so its domain checks pass. Only the version check saves us.
        slow.Orders.Add(TestData.Order(slowView));
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UnitOfWorkFor(slow).SaveChangesAsync(CancellationToken.None));

        Assert.Contains("modified by another request", ex.Message);
        await using var check = _db.CreateContext();
        Assert.Equal(0, (await check.Products.AsNoTracking().SingleAsync()).StockQuantity);
        Assert.Single(await check.Orders.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Duplicate_sku_is_a_conflict_even_when_the_application_check_is_bypassed()
    {
        await using (var first = _db.CreateContext())
        {
            first.Products.Add(TestData.Product("DUP"));
            await first.SaveChangesAsync();
        }

        await using var second = _db.CreateContext();
        second.Products.Add(TestData.Product("DUP"));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => UnitOfWorkFor(second).SaveChangesAsync(CancellationToken.None));

        Assert.Contains("SKU", ex.Message);
    }

    [Fact]
    public async Task Only_one_unresolved_alert_can_exist_per_product()
    {
        await using var context = _db.CreateContext();
        var product = TestData.Product("LOW", stock: 1, threshold: 5);
        context.Products.Add(product);
        context.Alerts.Add(Alert.RaiseLowStock(product));
        await UnitOfWorkFor(context).SaveChangesAsync(CancellationToken.None);

        await using var other = _db.CreateContext();
        var tracked = await other.Products.SingleAsync();
        other.Alerts.Add(Alert.RaiseLowStock(tracked));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => UnitOfWorkFor(other).SaveChangesAsync(CancellationToken.None));

        Assert.Contains("unresolved alert", ex.Message);
    }

    [Fact]
    public async Task A_resolved_alert_does_not_block_a_new_one()
    {
        await using var context = _db.CreateContext();
        var product = TestData.Product("LOW", stock: 1, threshold: 5);
        var first = Alert.RaiseLowStock(product);
        first.Resolve(_db.Time.GetUtcNow().UtcDateTime);
        context.Products.Add(product);
        context.Alerts.AddRange(first, Alert.RaiseLowStock(product));

        await UnitOfWorkFor(context).SaveChangesAsync(CancellationToken.None);

        await using var check = _db.CreateContext();
        Assert.Equal(2, await check.Alerts.CountAsync());
    }

    [Fact]
    public async Task A_failed_save_rolls_back_every_change_in_it()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Products.AddRange(TestData.Product("STOCKED", stock: 10), TestData.Product("TAKEN"));
            await seed.SaveChangesAsync();
        }

        var capture = new SqlCapture();
        await using var context = _db.CreateContext(capture);
        var stocked = await context.Products.SingleAsync(p => p.Sku == "STOCKED");

        // One perfectly valid unit of work (an order: two inserts plus a stock update)...
        context.Orders.Add(TestData.Order(stocked, quantity: 4));
        // ...and one change the database will refuse.
        context.Products.Add(TestData.Product("TAKEN"));

        await Assert.ThrowsAsync<ConflictException>(() => UnitOfWorkFor(context).SaveChangesAsync(CancellationToken.None));

        // The valid statements really were sent before the failure, so this proves they were undone, not skipped.
        Assert.Contains(capture.Commands, c => c.Sql.Contains("INSERT INTO \"Orders\""));

        await using var check = _db.CreateContext();
        Assert.Empty(await check.Orders.ToListAsync());
        Assert.Empty(await check.OrderItems.ToListAsync());
        Assert.Equal(10, (await check.Products.AsNoTracking().SingleAsync(p => p.Sku == "STOCKED")).StockQuantity);
        Assert.Equal(2, await check.Products.CountAsync());
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_by_the_database()
    {
        await using var context = _db.CreateContext();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Alerts" ("Id","ProductId","Type","Status","StockAtDetection","Threshold","Message","CreatedAt","UpdatedAt","Version")
            VALUES ('11111111-1111-1111-1111-111111111111','22222222-2222-2222-2222-222222222222','LowStock','Open',1,5,'m','2026-01-01','2026-01-01',1)
            """));

        Assert.Contains("FOREIGN KEY constraint failed", ex.ToString());
    }

    [Fact]
    public async Task A_product_referenced_by_an_order_cannot_be_hard_deleted()
    {
        Guid productId;
        await using (var seed = _db.CreateContext())
        {
            var product = TestData.Product("KEEP");
            seed.Orders.Add(TestData.Order(product));
            await seed.SaveChangesAsync();
            productId = product.Id;
        }

        await using var context = _db.CreateContext();
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("DELETE FROM \"Products\" WHERE \"Id\" = {0}", productId.ToString().ToUpperInvariant()));

        Assert.Contains("FOREIGN KEY constraint failed", ex.ToString());
    }

    [Fact]
    public async Task Deleting_an_order_removes_its_lines_but_never_its_products()
    {
        Guid orderId;
        await using (var seed = _db.CreateContext())
        {
            var order = TestData.Order(TestData.Product("LINES", stock: 10), quantity: 2);
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var context = _db.CreateContext())
        {
            context.Orders.Remove(await context.Orders.SingleAsync(o => o.Id == orderId));
            await context.SaveChangesAsync();
        }

        await using var check = _db.CreateContext();
        Assert.Empty(await check.OrderItems.ToListAsync());
        Assert.Single(await check.Products.ToListAsync());
    }

    [Fact]
    public async Task Audit_fields_and_version_are_maintained_on_insert_and_update()
    {
        var created = _db.Time.GetUtcNow().UtcDateTime;
        Guid id;
        await using (var context = _db.CreateContext())
        {
            var product = TestData.Product("AUD");
            context.Products.Add(product);
            await context.SaveChangesAsync();
            id = product.Id;

            Assert.Equal(created, product.CreatedAt);
            Assert.Equal(created, product.UpdatedAt);
            Assert.Equal(1, product.Version);
        }

        _db.Time.Advance(TimeSpan.FromHours(2));
        await using (var context = _db.CreateContext())
        {
            var product = await context.Products.SingleAsync(p => p.Id == id);
            product.AdjustStock(-1);
            await context.SaveChangesAsync();
        }

        await using var check = _db.CreateContext();
        var reloaded = await check.Products.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal(created, reloaded.CreatedAt);
        Assert.Equal(created.AddHours(2), reloaded.UpdatedAt);
        Assert.Equal(2, reloaded.Version);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAt.Kind);
        Assert.Equal(DateTimeKind.Utc, reloaded.UpdatedAt.Kind);
    }

    [Fact]
    public async Task Saving_without_changes_does_not_bump_the_version()
    {
        Guid id;
        await using (var context = _db.CreateContext())
        {
            var product = TestData.Product("QUIET");
            context.Products.Add(product);
            await context.SaveChangesAsync();
            id = product.Id;
        }

        await using (var context = _db.CreateContext())
        {
            _ = await context.Products.SingleAsync(p => p.Id == id);
            await context.SaveChangesAsync();
        }

        await using var check = _db.CreateContext();
        Assert.Equal(1, (await check.Products.AsNoTracking().SingleAsync(p => p.Id == id)).Version);
    }

    [Fact]
    public async Task The_database_itself_refuses_negative_stock()
    {
        await using var context = _db.CreateContext();
        context.Products.Add(TestData.Product("NEG", stock: 1));
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            context.Database.ExecuteSqlRawAsync("UPDATE \"Products\" SET \"StockQuantity\" = -1"));

        Assert.Contains("CK_Products_StockQuantity_NonNegative", ex.ToString());
    }

    [Fact]
    public async Task Order_lines_keep_their_price_and_product_after_a_round_trip()
    {
        Guid orderId;
        await using (var context = _db.CreateContext())
        {
            var product = TestData.Product("RT", stock: 10, price: 19.99m);
            var order = TestData.Order(product, quantity: 3);
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;
        }

        await using var check = _db.CreateContext();
        var stored = await check.Orders.Include(o => o.Items).ThenInclude(i => i.Product).SingleAsync(o => o.Id == orderId);

        var line = Assert.Single(stored.Items);
        Assert.Equal(19.99m, line.UnitPrice);
        Assert.Equal(3, line.Quantity);
        Assert.Equal("RT", line.Product.Sku);
        Assert.Equal(59.97m, stored.Total);
    }
}
