using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;

namespace Fulfillment.Application.Services;

public interface IOrderService
{
    Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, CancellationToken cancellationToken);

    Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Places an order and deducts stock atomically.</summary>
    Task<OrderResponse> PlaceAsync(CreateOrderRequest request, CancellationToken cancellationToken);

    Task<OrderResponse> ChangeStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);
}
