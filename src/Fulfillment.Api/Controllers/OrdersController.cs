using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Fulfillment.Api.Swagger.Examples;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace Fulfillment.Api.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    /// <summary>Lists orders, newest first, optionally filtered by <c>status</c>.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        await orders.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await orders.GetAsync(id, cancellationToken);

    /// <summary>Places an order. Stock is deducted immediately; if any line cannot be fulfilled nothing is deducted.</summary>
    [HttpPost]
    [SwaggerRequestExample(typeof(CreateOrderRequest), typeof(CreateOrderRequestExample))]
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
    [SwaggerRequestExample(typeof(UpdateOrderStatusRequest), typeof(UpdateOrderStatusRequestExample))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> ChangeStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken) =>
        await orders.ChangeStatusAsync(id, request, cancellationToken);
}
