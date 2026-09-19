using Fulfillment.Application.Abstractions;
using Fulfillment.Infrastructure.BackgroundJobs;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fulfillment.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Fulfillment";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        // The DbContext stamps audit fields with this. TryAdd so the layers don't depend on call order, and so a
        // host or test can substitute its own clock.
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<FulfillmentDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();

        var sentinelSection = configuration.GetSection(LowStockSentinelOptions.SectionName);
        services.AddOptions<LowStockSentinelOptions>()
            .Bind(sentinelSection)
            .Validate(o => o.IntervalSeconds > 0, $"{LowStockSentinelOptions.SectionName}:IntervalSeconds must be greater than zero.")
            .ValidateOnStart();

        if (sentinelSection.GetValue("Enabled", true))
        {
            services.AddHostedService<LowStockSentinel>();
        }

        return services;
    }
}
