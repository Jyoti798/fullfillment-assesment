using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Mapping;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Application.Services;

internal sealed class ProductService(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ILogger<ProductService> logger) : IProductService
{
    public async Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        var page = await products.ListAsync(query.Search, query.LowStockOnly, query.Page, query.PageSize, cancellationToken);
        return page.Map(p => p.ToResponse());
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await FindAsync(id, cancellationToken)).ToResponse();

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var sku = Product.NormalizeSku(request.Sku);
        if (await products.SkuExistsAsync(sku, cancellationToken))
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }

        var product = new Product(
            sku, request.Name, request.Description, request.UnitPrice, request.StockQuantity, request.ReorderThreshold);

        products.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {Sku} created with {StockQuantity} units in stock", product.Sku, product.StockQuantity);
        return product.ToResponse();
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);
        product.UpdateDetails(request.Name, request.Description, request.UnitPrice, request.ReorderThreshold, request.IsActive);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {Sku} updated", product.Sku);
        return product.ToResponse();
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await FindAsync(id, cancellationToken);
        if (!product.IsActive)
        {
            return; // already deactivated: DELETE is idempotent
        }

        product.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {Sku} deactivated", product.Sku);
    }

    public async Task<ProductResponse> AdjustStockAsync(Guid id, AdjustStockRequest request, CancellationToken cancellationToken)
    {
        if (request.Delta == 0)
        {
            throw new DomainValidationException(nameof(request.Delta), "Delta must not be zero.");
        }

        // A delta (not an absolute quantity) is applied to fresh data on every attempt, so two racing adjustments
        // both land instead of one silently overwriting the other.
        return await unitOfWork.ExecuteAsync(async () =>
        {
            var product = await FindAsync(id, cancellationToken);
            var before = product.StockQuantity;
            product.AdjustStock(request.Delta);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Stock for {Sku} adjusted by {Delta} ({Before} -> {After}). Reason: {Reason}",
                product.Sku, request.Delta, before, product.StockQuantity, request.Reason ?? "not given");
            return product.ToResponse();
        }, cancellationToken);
    }

    private async Task<Product> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await products.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Product), id);
}
