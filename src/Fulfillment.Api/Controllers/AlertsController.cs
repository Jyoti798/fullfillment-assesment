using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(IAlertService alerts) : ControllerBase
{
    /// <summary>Lists alerts, newest first, optionally filtered by <c>status</c> and <c>productId</c>.</summary>
    /// <remarks>
    /// Alerts are raised (and resolved when stock recovers) by the Low Stock Sentinel. Demo flow: trigger
    /// low stock, execute this endpoint, then copy an alert <c>id</c> into acknowledge or resolve.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AlertResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AlertResponse>>> List([FromQuery] AlertListQuery query, CancellationToken cancellationToken) =>
        await alerts.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlertResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await alerts.GetAsync(id, cancellationToken);

    /// <summary>Marks an open alert as seen. The alert stays active until it is resolved.</summary>
    /// <remarks>
    /// Requires an existing alert ID. Use <c>GET /api/alerts</c>, copy an open alert <c>id</c>, paste it into
    /// the path parameter, and execute. This endpoint has no request body.
    /// </remarks>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlertResponse>> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        await alerts.AcknowledgeAsync(id, cancellationToken);

    /// <summary>Closes an open or acknowledged alert by hand.</summary>
    /// <remarks>
    /// Requires an existing alert ID. Use <c>GET /api/alerts</c>, copy an alert <c>id</c>, paste it into the
    /// path parameter, and execute. This endpoint has no request body.
    ///
    /// Allowed even while stock is still low. In that case the Low Stock Sentinel raises a fresh alert on its
    /// next scan, because alerts always mirror the real stock levels; resolving does not silence a product that
    /// genuinely needs restocking. Alerts are also resolved automatically once stock recovers.
    /// </remarks>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlertResponse>> Resolve(Guid id, CancellationToken cancellationToken) =>
        await alerts.ResolveAsync(id, cancellationToken);
}
