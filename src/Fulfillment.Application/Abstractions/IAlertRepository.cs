using Fulfillment.Application.Common;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;

namespace Fulfillment.Application.Abstractions;

public interface IAlertRepository
{
    /// <summary>Loads the alert together with its product.</summary>
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<Alert>> ListAsync(AlertStatus? status, Guid? productId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>All open or acknowledged alerts, each with its product loaded.</summary>
    Task<IReadOnlyList<Alert>> GetUnresolvedAsync(CancellationToken cancellationToken);

    void Add(Alert alert);
}
