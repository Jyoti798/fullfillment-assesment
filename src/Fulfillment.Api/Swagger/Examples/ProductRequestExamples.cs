using Fulfillment.Application.Dtos;
using Swashbuckle.AspNetCore.Filters;

namespace Fulfillment.Api.Swagger.Examples;

public sealed class CreateProductRequestExample : IExamplesProvider<CreateProductRequest>
{
    public CreateProductRequest GetExamples() => new()
    {
        Sku = "SKU-1001",
        Name = "Demo Product",
        Description = "Sample product for testing",
        UnitPrice = 100m,
        StockQuantity = 50,
        ReorderThreshold = 10
    };
}

public sealed class UpdateProductRequestExample : IExamplesProvider<UpdateProductRequest>
{
    public UpdateProductRequest GetExamples() => new()
    {
        Name = "Updated Demo Product",
        Description = "Updated product details for testing",
        UnitPrice = 120m,
        ReorderThreshold = 8,
        IsActive = true
    };
}

public sealed class AdjustStockRequestExample : IExamplesProvider<AdjustStockRequest>
{
    public AdjustStockRequest GetExamples() => new()
    {
        Delta = 10,
        Reason = "Supplier delivery"
    };
}
