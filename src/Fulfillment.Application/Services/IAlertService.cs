using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;

namespace Fulfillment.Application.Services;

public interface IAlertService
{
    Task<PagedResult<AlertResponse>> ListAsync(AlertListQuery query, CancellationToken cancellationToken);

    Task<AlertResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Marks an open alert as seen. It stays active until it is resolved.</summary>
    Task<AlertResponse> AcknowledgeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Closes an unresolved alert by hand. This is allowed even while stock is still low; in that case the Low Stock
    /// Sentinel raises a fresh alert on its next scan, because the alerts always mirror the real stock levels.
    /// </summary>
    Task<AlertResponse> ResolveAsync(Guid id, CancellationToken cancellationToken);
}
