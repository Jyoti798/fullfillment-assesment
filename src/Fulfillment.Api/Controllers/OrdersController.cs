using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Lists orders, newest first, optionally filtered by <c>status</c>.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        await orders.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken);

    /// <summary>Places an order. Stock is deducted immediately; if any line cannot be fulfilled nothing is deducted.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Place(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.PlaceAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>
    /// Moves the order through its lifecycle: Pending, Confirmed, Shipped, Delivered. Orders can be
    /// Cancelled from Pending or Confirmed, which returns the reserved stock.
    /// </summary>
    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> ChangeStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        await orders.ChangeStatusAsync(id, request, cancellationToken);
}
