using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository(FulfillmentDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<PagedResult<Order>> ListAsync(OrderStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        IQueryable<Order> query = db.Orders
            .AsNoTracking()
            .Include(o => o.Items).ThenInclude(i => i.Product);

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        return query
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public void Add(Order order) => db.Orders.Add(order);
}
