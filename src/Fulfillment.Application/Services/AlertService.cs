using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Mapping;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Application.Services;

internal sealed class AlertService(
    IAlertRepository alerts,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<AlertService> logger) : IAlertService
{
    public async Task<PagedResult<AlertResponse>> ListAsync(AlertListQuery query, CancellationToken cancellationToken)
    {
        var page = await alerts.ListAsync(query.Status, query.ProductId, query.Page, query.PageSize, cancellationToken);
        return page.Map(a => a.ToResponse());
    }

    public async Task<AlertResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await FindAsync(id, cancellationToken)).ToResponse();

    // Both mutations run through ExecuteAsync: when two people (or the sentinel) race on one alert, the loser re-reads
    // it and reports what really happened ("already resolved") rather than a generic "modified by another request".
    public Task<AlertResponse> AcknowledgeAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var alert = await FindAsync(id, cancellationToken);
            alert.Acknowledge(timeProvider.GetUtcNow().UtcDateTime);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Alert {AlertId} for {Sku} acknowledged", alert.Id, alert.Product.Sku);
            return alert.ToResponse();
        }, cancellationToken);

    public Task<AlertResponse> ResolveAsync(Guid id, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var alert = await FindAsync(id, cancellationToken);
            alert.Resolve(timeProvider.GetUtcNow().UtcDateTime);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Alert {AlertId} for {Sku} resolved manually (stock {StockQuantity}, threshold {Threshold})",
                alert.Id, alert.Product.Sku, alert.Product.StockQuantity, alert.Product.ReorderThreshold);
            return alert.ToResponse();
        }, cancellationToken);

    private async Task<Alert> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await alerts.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Alert), id);
}
