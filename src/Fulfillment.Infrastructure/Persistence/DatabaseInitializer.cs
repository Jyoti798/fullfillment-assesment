using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Applies pending migrations and, when enabled, loads sample data for local demos.</summary>
    public static async Task InitializeAsync(
        IServiceProvider services, bool migrate, bool seed, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        if (migrate)
        {
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrations applied");
        }

        if (seed)
        {
            var changed = false;

            if (!await db.Products.AnyAsync(cancellationToken))
            {
                db.Products.AddRange(SampleProducts());
                changed = true;
                logger.LogInformation("Sample catalogue queued for seeding");
            }

            changed |= await EnsureSwaggerDemoProductAsync(db, logger, cancellationToken);

            if (changed)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Sample catalogue seeded");
            }
        }
    }

    private static async Task<bool> EnsureSwaggerDemoProductAsync(
        FulfillmentDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(p => p.Id == DemoSeedData.SwaggerOrderProductId, cancellationToken))
        {
            return false;
        }

        if (await db.Products.AnyAsync(p => p.Sku == DemoSeedData.SwaggerOrderProductSku, cancellationToken))
        {
            logger.LogWarning(
                "Swagger demo product SKU {Sku} already exists with a different ID; use GET /api/products to copy an available product ID for order demos.",
                DemoSeedData.SwaggerOrderProductSku);
            return false;
        }

        var product = DemoSeedData.CreateSwaggerOrderProduct();
        db.Products.Add(product);
        db.Entry(product).Property(p => p.Id).CurrentValue = DemoSeedData.SwaggerOrderProductId;

        return true;
    }

    // A mix of healthy and already-low products so the sentinel has something to report on first run.
    private static IEnumerable<Product> SampleProducts() =>
    [
        new("KB-100", "Mechanical Keyboard", "Tenkeyless, brown switches", 89.99m, 40, 10),
        new("MS-200", "Wireless Mouse", "Ergonomic, 2.4GHz", 29.50m, 75, 15),
        new("MN-300", "27\" Monitor", "1440p IPS panel", 279.00m, 12, 5),
        new("HD-400", "USB-C Hub", "7-in-1 with HDMI and card reader", 44.90m, 4, 10),
        new("WC-500", "HD Webcam", "1080p with privacy shutter", 59.00m, 2, 5),
        new("CB-600", "HDMI Cable 2m", null, 9.99m, 150, 30)
    ];
}
