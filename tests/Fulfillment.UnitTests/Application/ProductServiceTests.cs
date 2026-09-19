using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Fulfillment.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Application;

public class ProductServiceTests
{
    private readonly TestDatabase _db = new();

    private ProductService CreateService(FulfillmentDbContext context) => new(
        new ProductRepository(context),
        new UnitOfWork(context, NullLogger<UnitOfWork>.Instance),
        NullLogger<ProductService>.Instance);

    private async Task<ProductResponse> CreateAsync(string sku, string name = "Widget", int stock = 10, int threshold = 2)
    {
        await using var context = _db.CreateContext();
        return await CreateService(context).CreateAsync(
            new CreateProductRequest { Sku = sku, Name = name, UnitPrice = 5m, StockQuantity = stock, ReorderThreshold = threshold },
            CancellationToken.None);
    }

    [Fact]
    public async Task CreateAsync_stores_a_normalised_sku_and_reports_low_stock()
    {
        var created = await CreateAsync("  ab-1 ", stock: 2, threshold: 2);

        Assert.Equal("AB-1", created.Sku);
        Assert.True(created.IsLowStock);
        Assert.True(created.IsActive);
        Assert.Equal(DateTimeKind.Utc, created.CreatedAt.Kind);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_sku_regardless_of_case()
    {
        await CreateAsync("ABC-1");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateAsync("abc-1"));

        Assert.Contains("ABC-1", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_changes_details_and_can_reactivate()
    {
        var created = await CreateAsync("UP-1");

        await using (var context = _db.CreateContext())
        {
            await CreateService(context).DeactivateAsync(created.Id, CancellationToken.None);
        }

        await using var update = _db.CreateContext();
        var updated = await CreateService(update).UpdateAsync(
            created.Id,
            new UpdateProductRequest { Name = "Renamed", UnitPrice = 9.99m, ReorderThreshold = 7, IsActive = true },
            CancellationToken.None);

        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(9.99m, updated.UnitPrice);
        Assert.Equal(7, updated.ReorderThreshold);
        Assert.True(updated.IsActive);
        Assert.Equal(10, updated.StockQuantity);
    }

    [Fact]
    public async Task DeactivateAsync_is_idempotent()
    {
        var created = await CreateAsync("DEL-1");

        for (var i = 0; i < 2; i++)
        {
            await using var context = _db.CreateContext();
            await CreateService(context).DeactivateAsync(created.Id, CancellationToken.None);
        }

        await using var check = _db.CreateContext();
        Assert.False((await CreateService(check).GetAsync(created.Id, CancellationToken.None)).IsActive);
    }

    [Fact]
    public async Task AdjustStockAsync_adds_and_removes_stock()
    {
        var created = await CreateAsync("STK-1", stock: 10);

        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var afterAdd = await service.AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = 5, Reason = "delivery" }, CancellationToken.None);
        var afterRemove = await service.AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = -12 }, CancellationToken.None);

        Assert.Equal(15, afterAdd.StockQuantity);
        Assert.Equal(3, afterRemove.StockQuantity);
    }

    [Fact]
    public async Task AdjustStockAsync_survives_losing_a_race_to_another_adjustment()
    {
        var created = await CreateAsync("RACE-1", stock: 10);
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = 5 }, CancellationToken.None);
        });

        await using var context = _db.CreateContext(race);
        var result = await CreateService(context).AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = 3 }, CancellationToken.None);

        Assert.Equal(18, result.StockQuantity); // neither adjustment was lost
    }

    [Fact]
    public async Task AdjustStockAsync_rejects_zero_and_negative_results()
    {
        var created = await CreateAsync("STK-2", stock: 3);

        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = 0 }, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AdjustStockAsync(created.Id, new AdjustStockRequest { Delta = -4 }, CancellationToken.None));
    }

    [Fact]
    public async Task Operations_on_unknown_products_throw_NotFound()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeactivateAsync(id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.AdjustStockAsync(id, new AdjustStockRequest { Delta = 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_searches_name_and_sku_case_insensitively()
    {
        await CreateAsync("KB-100", "Mechanical Keyboard");
        await CreateAsync("MS-200", "Wireless Mouse");

        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var byName = await service.ListAsync(new ProductListQuery { Search = "keyBOARD" }, CancellationToken.None);
        var bySku = await service.ListAsync(new ProductListQuery { Search = "ms-2" }, CancellationToken.None);

        Assert.Equal("KB-100", Assert.Single(byName.Items).Sku);
        Assert.Equal("MS-200", Assert.Single(bySku.Items).Sku);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task ListAsync_treats_like_wildcards_in_the_search_term_literally(string search)
    {
        await CreateAsync("A-1", "Plain product");

        await using var context = _db.CreateContext();
        var result = await CreateService(context).ListAsync(new ProductListQuery { Search = search }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ListAsync_filters_low_stock_and_pages_in_name_order()
    {
        await CreateAsync("C", "Charlie", stock: 1, threshold: 5);
        await CreateAsync("A", "Alpha", stock: 1, threshold: 5);
        await CreateAsync("B", "Bravo", stock: 50, threshold: 5);
        await CreateAsync("D", "Delta", stock: 50, threshold: 5);

        await using var context = _db.CreateContext();
        var service = CreateService(context);

        var low = await service.ListAsync(new ProductListQuery { LowStockOnly = true }, CancellationToken.None);
        Assert.Equal(["Alpha", "Charlie"], low.Items.Select(p => p.Name).ToArray());
        Assert.Equal(2, low.TotalCount);

        var page2 = await service.ListAsync(new ProductListQuery { Page = 2, PageSize = 3 }, CancellationToken.None);
        Assert.Equal(["Delta"], page2.Items.Select(p => p.Name).ToArray());
        Assert.Equal(4, page2.TotalCount);
    }
}
