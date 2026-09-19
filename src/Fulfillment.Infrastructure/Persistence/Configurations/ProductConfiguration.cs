using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", t =>
        {
            // Last line of defence against overselling, independent of application logic.
            t.HasCheckConstraint("CK_Products_StockQuantity_NonNegative", "\"StockQuantity\" >= 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000);
        builder.Property(p => p.UnitPrice).HasPrecision(18, 2);

        builder.HasIndex(p => p.Sku).IsUnique();

        // The catalogue listing is ordered by name; without this SQLite sorts every product for every page.
        builder.HasIndex(p => p.Name);

        // Serves the sentinel's scan for active products at or below their reorder threshold. Comparing two columns
        // can't be seeked, so instead the index is *partial*: its filter is the query's own predicate, so it holds
        // only the low-stock products (a tiny fraction of the catalogue) and SQLite scans just those instead of every
        // product row. The filter must match ProductRepository.GetLowStockAsync exactly; a test guards that.
        builder.HasIndex(p => p.StockQuantity)
            .HasDatabaseName("IX_Products_LowStock")
            .HasFilter("\"IsActive\" AND \"StockQuantity\" <= \"ReorderThreshold\"");

        builder.Ignore(p => p.IsLowStock);
    }
}
