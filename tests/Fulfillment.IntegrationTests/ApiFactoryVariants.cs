using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fulfillment.IntegrationTests;

/// <summary>The app running in the Development environment, where Swagger is switched on.</summary>
public sealed class DevelopmentApiFactory : FulfillmentApiFactory
{
    protected override string EnvironmentName => "Development";
}

/// <summary>
/// The app with a product service that always blows up, to exercise the unhandled-exception path and prove
/// that nothing about the failure leaks into the response.
/// </summary>
public sealed class ThrowingApiFactory : FulfillmentApiFactory
{
    public const string SecretDetail = "boom: connection string is Server=prod-db;Password=hunter2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProductService>();
            services.AddScoped<IProductService, ExplodingProductService>();
        });
    }

    private sealed class ExplodingProductService : IProductService
    {
        private static Exception Boom() => new InvalidOperationException(SecretDetail);

        public Task<PagedResult<ProductResponse>> ListAsync(ProductListQuery query, CancellationToken cancellationToken) => throw Boom();

        public Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken) => throw Boom();

        public Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken) => throw Boom();

        public Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) => throw Boom();

        public Task DeactivateAsync(Guid id, CancellationToken cancellationToken) => throw Boom();

        public Task<ProductResponse> AdjustStockAsync(Guid id, AdjustStockRequest request, CancellationToken cancellationToken) => throw Boom();
    }
}
