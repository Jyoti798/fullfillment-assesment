using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Message).IsRequired().HasMaxLength(500);

        builder.HasOne(a => a.Product)
            .WithMany()
            .HasForeignKey(a => a.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one unresolved alert per product, enforced by the database so that the sentinel stays
        // idempotent even if two instances scan at the same time.
        builder.HasIndex(a => a.ProductId)
            .IsUnique()
            .HasFilter("\"Status\" <> 'Resolved'");

        builder.HasIndex(a => new { a.Status, a.CreatedAt });

        // The unique index above only covers unresolved alerts, so this one serves "all alerts for a product".
        builder.HasIndex(a => new { a.ProductId, a.CreatedAt });

        builder.Ignore(a => a.IsUnresolved);
    }
}
