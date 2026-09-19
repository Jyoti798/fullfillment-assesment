using Fulfillment.Domain.Entities;

namespace Fulfillment.Infrastructure.Persistence;

public static class DemoSeedData
{
    public static readonly Guid SwaggerOrderProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string SwaggerOrderProductSku = "DEMO-ORDER-001";

    public static Product CreateSwaggerOrderProduct() =>
        new(
            SwaggerOrderProductSku,
            "Swagger Demo Product",
            "Stable seeded product used by Swagger request examples",
            100m,
            50,
            10);
}
