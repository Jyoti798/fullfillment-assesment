using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Infrastructure.Persistence.Repositories;

internal sealed class AlertRepository(FulfillmentDbContext db) : IAlertRepository
{
    public Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Alerts.Include(a => a.Product).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<PagedResult<Alert>> ListAsync(
        AlertStatus? status, Guid? productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Alerts.AsNoTracking().Include(a => a.Product).AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (productId.HasValue)
        {
            query = query.Where(a => a.ProductId == productId.Value);
        }

        return query
            .OrderByDescending(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToPagedResultAsync(page, pageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken cancellationToken) =>
        await db.Alerts
            .Include(a => a.Product)
            .Where(a => a.Status != AlertStatus.Resolved)
            .ToListAsync(cancellationToken);

    public void Add(Alert alert) => db.Alerts.Add(alert);
}
