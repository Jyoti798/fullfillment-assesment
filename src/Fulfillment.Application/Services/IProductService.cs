using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;

namespace Fulfillment.Application.Services;

public interface IProductService
{
    Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken cancellationToken);

    Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete: the product is deactivated so historical orders stay intact.</summary>
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);

    Task<ProductResponse> AdjustStockAsync(Guid id, AdjustStockRequest request, CancellationToken cancellationToken);
}
