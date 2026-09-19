using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Mapping;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Application.Services;

internal sealed class OrderService(
    IOrderRepository orders,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<PagedResult<OrderResponse>> ListAsync(OrderListQuery query, CancellationToken cancellationToken)
    {
        var page = await orders.ListAsync(query.Status, query.Page, query.PageSize, cancellationToken);
        return page.Map(o => o.ToResponse());
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await FindAsync(id, cancellationToken)).ToResponse();

    // Runs through ExecuteAsync so that losing a race to another order for the same product re-runs the whole thing on
    // fresh stock: it still succeeds if there is enough, and fails with a genuine "insufficient stock" if there isn't.
    public Task<OrderResponse> PlaceAsync(CreateOrderRequest request, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var found = (await products.GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);

            var unknown = productIds.Where(id => !found.ContainsKey(id)).ToList();
            if (unknown.Count > 0)
            {
                throw new DomainValidationException("Items", $"Unknown product id(s): {string.Join(", ", unknown)}.");
            }

            var lines = request.Items.Select(i => (found[i.ProductId], i.Quantity)).ToList();
            var order = Order.Place(request.CustomerName, request.CustomerEmail, lines);

            orders.Add(order);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Order {OrderId} placed: {ItemCount} line(s), total {Total}", order.Id, order.Items.Count, order.Total);
            return order.ToResponse();
        }, cancellationToken);

    // Retried for the same reason. It also stops two racing cancels from both returning the stock: the loser re-reads
    // an already-cancelled order and gets a genuine "cannot change from Cancelled to Cancelled".
    public Task<OrderResponse> ChangeStatusAsync(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var order = await FindAsync(id, cancellationToken);
            var previous = order.Status;

            order.ChangeStatus(request.Status);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Order {OrderId} moved from {From} to {To}", order.Id, previous, order.Status);
            return order.ToResponse();
        }, cancellationToken);

    private async Task<Order> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await orders.GetByIdAsync(id, cancellationToken) ?? throw new NotFoundException(nameof(Order), id);
}
