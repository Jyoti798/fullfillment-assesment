using Fulfillment.Application.Dtos;
using Fulfillment.Domain.Entities;

namespace Fulfillment.Application.Mapping;

internal static class MappingExtensions
{
    public static ProductResponse ToResponse(this Product p) => new(
        p.Id, p.Sku, p.Name, p.Description, p.UnitPrice, p.StockQuantity,
        p.ReorderThreshold, p.IsActive, p.IsLowStock, p.CreatedAt, p.UpdatedAt);

    public static OrderResponse ToResponse(this Order o) => new(
        o.Id,
        o.CustomerName,
        o.CustomerEmail,
        o.Status,
        o.Total,
        o.Items.Select(i => new OrderItemResponse(
            i.ProductId, i.Product.Sku, i.Product.Name, i.Quantity, i.UnitPrice, i.LineTotal)).ToList(),
        o.CreatedAt,
        o.UpdatedAt);

    public static AlertResponse ToResponse(this Alert a) => new(
        a.Id, a.ProductId, a.Product.Sku, a.Product.Name, a.Type, a.Status,
        a.StockAtDetection, a.Threshold, a.Product.StockQuantity, a.Message,
        a.CreatedAt, a.AcknowledgedAt, a.ResolvedAt);
}
