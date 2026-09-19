using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;

namespace Fulfillment.Application.Abstractions;

public interface IOrderRepository
{
    /// <summary>Loads the order together with its items and their products.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Order>> ListAsync(OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    void Add(Order order);
}
