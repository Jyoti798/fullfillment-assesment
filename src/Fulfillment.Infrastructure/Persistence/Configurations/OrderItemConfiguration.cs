using Fulfillment.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fulfillment.Infrastructure.Persistence.Configurations;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "\"Quantity\" > 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);

        // Products are soft-deleted, so an order line must never lose its product.
        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.ProductId);

        builder.Ignore(i => i.LineTotal);
    }
}
