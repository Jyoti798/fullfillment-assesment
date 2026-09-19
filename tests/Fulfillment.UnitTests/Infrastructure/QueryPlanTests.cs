using Fulfillment.Domain.Enums;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.UnitTests.Infrastructure;

/// <summary>
/// Guards the performance-critical indexes. Each test runs a real repository method, captures the SQL EF
/// really sends, and asks SQLite for its query plan on a database with enough rows for indexes to matter.
/// If someone edits a query or an index so that they no longer line up, these fail.
/// </summary>
public class QueryPlanTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public QueryPlanTests()
    {
        using var context = _db.CreateContext();

        // 20,000 products, ~2% low on stock and ~10% inactive; 5,000 orders; 300 alerts (2/3 unresolved) on the low ones.
        context.Database.ExecuteSqlRaw("""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 20000)
            INSERT INTO "Products" ("Id","Sku","Name","UnitPrice","StockQuantity","ReorderThreshold","IsActive","CreatedAt","UpdatedAt","Version")
            SELECT printf('%08X-0000-4000-8000-%012X', i, i), printf('SKU-%06d', i), 'Product ' || i, 9.99,
                   CASE WHEN i % 50 = 0 THEN i % 6 ELSE 100 + i % 400 END, 10,
                   CASE WHEN i % 10 = 3 THEN 0 ELSE 1 END, '2026-01-01 00:00:00', '2026-01-01 00:00:00', 1
            FROM n;
            """);
        context.Database.ExecuteSqlRaw("""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 5000)
            INSERT INTO "Orders" ("Id","CustomerName","CustomerEmail","Status","CreatedAt","UpdatedAt","Version")
            SELECT printf('%08X-0000-4000-8000-%012X', i, i), 'Ada', 'ada@example.com',
                   CASE i % 5 WHEN 0 THEN 'Pending' WHEN 1 THEN 'Confirmed' WHEN 2 THEN 'Shipped' WHEN 3 THEN 'Delivered' ELSE 'Cancelled' END,
                   datetime('2026-01-01', '+' || i || ' minutes'), '2026-01-01 00:00:00', 1
            FROM n;
            """);
        context.Database.ExecuteSqlRaw("""
            INSERT INTO "Alerts" ("Id","ProductId","Type","Status","StockAtDetection","Threshold","Message","CreatedAt","UpdatedAt","Version")
            SELECT printf('%08X-0000-4000-8000-%012X', ROW_NUMBER() OVER (ORDER BY "Id"), ROW_NUMBER() OVER (ORDER BY "Id")), "Id", 'LowStock',
                   CASE WHEN ROW_NUMBER() OVER (ORDER BY "Id") % 3 = 0 THEN 'Resolved' ELSE 'Open' END,
                   "StockQuantity", 10, 'msg', '2026-01-01 00:00:00', '2026-01-01 00:00:00', 1
            FROM "Products" WHERE "StockQuantity" <= "ReorderThreshold" AND "IsActive" LIMIT 300;
            """);
        context.Database.ExecuteSqlRaw("ANALYZE");
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Runs <paramref name="action"/>, then returns SQLite's plan for the last SELECT that satisfies <paramref name="pick"/>.</summary>
    private async Task<string> PlanOfAsync(
        Func<FulfillmentDbContext, Task> action, Func<CapturedCommand, bool> pick)
    {
        var capture = new SqlCapture();
        await using var context = _db.CreateContext(capture);
        await action(context);

        var query = capture.Commands.Last(pick);

        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + query.Sql;
        foreach (var (name, value) in query.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var plan = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plan.Add(reader.GetString(3)); // the "detail" column
        }

        return string.Join(" | ", plan);
    }

    [Fact]
    public async Task The_sentinels_low_stock_scan_reads_the_small_partial_index_not_the_whole_table()
    {
        var plan = await PlanOfAsync(
            c => new ProductRepository(c).GetLowStockAsync(CancellationToken.None),
            q => q.Sql.Contains("FROM \"Products\""));

        Assert.Contains("USING INDEX IX_Products_LowStock", plan);
    }

    [Fact]
    public async Task The_low_stock_index_holds_only_the_rows_the_scan_needs()
    {
        await using var context = _db.CreateContext();

        var total = await context.Products.CountAsync();
        var lowActive = (await new ProductRepository(context).GetLowStockAsync(CancellationToken.None)).Count;

        // Sanity check of the test data: the index is only worthwhile if it is far smaller than the table.
        Assert.Equal(20_000, total);
        Assert.InRange(lowActive, 1, total / 20);
    }

    [Fact]
    public async Task Listing_all_alerts_for_a_product_seeks_by_product()
    {
        var plan = await PlanOfAsync(
            c => new AlertRepository(c).ListAsync(null, Guid.NewGuid(), 1, 20, CancellationToken.None),
            q => q.Sql.Contains("ORDER BY") && q.Sql.Contains("\"ProductId\" ="));

        Assert.Contains("SEARCH", plan);
        Assert.Contains("IX_Alerts_ProductId_CreatedAt", plan);
    }

    [Fact]
    public async Task Listing_orders_newest_first_walks_the_created_at_index_instead_of_sorting_everything()
    {
        var plan = await PlanOfAsync(
            c => new OrderRepository(c).ListAsync(null, 1, 20, CancellationToken.None),
            q => q.Sql.Contains("LIMIT") && q.Sql.Contains("FROM \"Orders\""));

        Assert.Contains("IX_Orders_CreatedAt", plan);
    }

    [Fact]
    public async Task Listing_orders_by_status_seeks_the_status_index()
    {
        var plan = await PlanOfAsync(
            c => new OrderRepository(c).ListAsync(OrderStatus.Pending, 1, 20, CancellationToken.None),
            q => q.Sql.Contains("LIMIT") && q.Sql.Contains("FROM \"Orders\""));

        Assert.Contains("IX_Orders_Status_CreatedAt (Status=?)", plan);
    }

    [Fact]
    public async Task Listing_products_by_name_walks_the_name_index_instead_of_sorting_the_whole_catalogue()
    {
        var plan = await PlanOfAsync(
            c => new ProductRepository(c).ListAsync(null, false, 1, 20, CancellationToken.None),
            q => q.Sql.Contains("LIMIT") && q.Sql.Contains("ORDER BY"));

        Assert.Contains("IX_Products_Name", plan);
    }

    [Fact]
    public async Task Looking_up_a_product_by_sku_uses_the_unique_index()
    {
        var plan = await PlanOfAsync(
            c => new ProductRepository(c).SkuExistsAsync("SKU-000123", CancellationToken.None),
            q => q.Sql.Contains("\"Sku\""));

        Assert.Contains("IX_Products_Sku", plan);
    }
}
