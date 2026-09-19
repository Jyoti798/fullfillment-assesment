using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(FulfillmentDbContext db) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<bool> SkuExistsAsync(string normalizedSku, CancellationToken cancellationToken) =>
        db.Products.AnyAsync(p => p.Sku == normalizedSku, cancellationToken);

    public Task<PagedResult<Product>> ListAsync(
        string? search, bool lowStockOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern, "\\") || EF.Functions.Like(p.Sku, pattern, "\\"));
        }

        if (lowStockOnly)
        {
            query = query.Where(p => p.StockQuantity <= p.ReorderThreshold);
        }

        return query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken cancellationToken) =>
        await db.Products
            .Where(p => p.IsActive && p.StockQuantity <= p.ReorderThreshold)
            .ToListAsync(cancellationToken);

    public void Add(Product product) => db.Products.Add(product);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
