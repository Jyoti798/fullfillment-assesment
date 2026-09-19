using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Abstractions;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(string normalizedSku, CancellationToken cancellationToken);

    Task<PagedResult<Product>> ListAsync(string? search, bool lowStockOnly, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Active products whose stock is at or below their reorder threshold.</summary>
    Task<IReadOnlyList<Product>> GetLowStockAsync(CancellationToken cancellationToken);

    void Add(Product product);
}
