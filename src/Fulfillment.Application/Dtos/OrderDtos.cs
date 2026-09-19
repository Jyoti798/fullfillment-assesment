
using System.ComponentModel.DataAnnotations;
using Fulfillment.Domain.Enums;

namespace Fulfillment.Application.Dtos;

public sealed record OrderItemResponse(
    Guid ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    string CustomerName,
    string CustomerEmail,
    OrderStatus Status,
    decimal Total,
    IReadOnlyList<OrderItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed class OrderItemRequest
{
    public Guid ProductId { get; init; }

    [Range(1, 100_000)]
    public int Quantity { get; init; }
}

public sealed class CreateOrderRequest
{
    [Required, StringLength(200)]
    public string CustomerName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(320)]
    public string CustomerEmail { get; init; } = string.Empty;

    [Required, MinLength(1), MaxLength(100)]
    public List<OrderItemRequest> Items { get; init; } = [];
}

public sealed class UpdateOrderStatusRequest
{
    [EnumDataType(typeof(OrderStatus))]
    public OrderStatus Status { get; init; }
}

public sealed class OrderListQuery : PagedQuery
{
    public OrderStatus? Status { get; init; }
}
