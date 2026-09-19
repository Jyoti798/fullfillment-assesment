using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Application.Services;
using Fulfillment.Api.Swagger.Examples;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace Fulfillment.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(IProductService products) : ControllerBase
{
    /// <summary>Lists products, with optional search (name or SKU) and low-stock filtering.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List([FromQuery] ProductListQuery query, CancellationToken cancellationToken) =>
        await products.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        await products.GetAsync(id, cancellationToken);

    [HttpPost]
    [SwaggerRequestExample(typeof(CreateProductRequest), typeof(CreateProductRequestExample))]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var created = await products.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [SwaggerRequestExample(typeof(UpdateProductRequest), typeof(UpdateProductRequestExample))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        await products.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deactivates the product (soft delete). Existing orders keep referencing it.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await products.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Adjusts stock by a signed amount: positive to add (a delivery), negative to remove (shrinkage).</summary>
    /// <remarks>
    /// The body is a delta rather than an absolute quantity, so two people adjusting at once both take effect
    /// instead of one silently overwriting the other. Stock can never go below zero.
    /// </remarks>
    [HttpPatch("{id:guid}/stock")]
    [SwaggerRequestExample(typeof(AdjustStockRequest), typeof(AdjustStockRequestExample))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> AdjustStock(Guid id, AdjustStockRequest request, CancellationToken cancellationToken) =>
        await products.AdjustStockAsync(id, request, cancellationToken);
}
