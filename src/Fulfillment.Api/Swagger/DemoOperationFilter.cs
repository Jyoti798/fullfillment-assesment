using System.Text.Json.Nodes;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Fulfillment.Api.Swagger;

/// <summary>
/// Makes the Swagger UI usable for a live demo: pre-fills the seeded demo product id where that is safe, and adds
/// short "what to do next" guidance to the operations. The alert operations are documented in their controller
/// <c>&lt;remarks&gt;</c> instead, so they are deliberately not handled here.
/// </summary>
public sealed class DemoOperationFilter : IOperationFilter
{
    private static readonly string DemoProductId = DemoSeedData.SwaggerOrderProductId.ToString();

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        var method = context.ApiDescription.HttpMethod;

        ImproveParameters(operation, path, method);
        ImproveOperationDescription(operation, path, method);
    }

    private static void ImproveParameters(OpenApiOperation operation, string path, string? method)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (IsNamed(parameter, "id") && path.StartsWith("api/products/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    $"Product ID. In Development, the seeded Swagger demo product ID is {DemoProductId}. You can also copy an ID from GET /api/products or POST /api/products.");

                // Pre-fill it so GET, PUT and PATCH can be executed in one click. Not for DELETE: a stray click there
                // would deactivate the demo product, and the order example would then fail with 409.
                if (method != "DELETE" && parameter is OpenApiParameter concrete)
                {
                    concrete.Example = JsonValue.Create(DemoProductId);
                }
            }

            if (IsNamed(parameter, "id") && path.StartsWith("api/orders/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    "Order ID. Create an order with POST /api/orders, then copy the returned id into this field.");
            }

            if (IsNamed(parameter, "id") && path.StartsWith("api/alerts/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    "Alert ID. Use GET /api/alerts, copy an existing alert id, then execute this operation.");
            }

            // Described but deliberately not pre-filled: pre-filling would hide every other alert on the first click.
            if (IsNamed(parameter, "productId"))
            {
                parameter.Description = Append(
                    parameter.Description,
                    $"Optional product filter. In Development, use {DemoProductId} for the seeded Swagger demo product.");
            }

            if (IsNamed(parameter, "status") && path.StartsWith("api/orders", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(parameter.Description, "Optional order status filter. Valid values include Pending, Confirmed, Shipped, Delivered, and Cancelled.");
            }

            if (IsNamed(parameter, "status") && path.StartsWith("api/alerts", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(parameter.Description, "Optional alert status filter. Valid values are Open, Acknowledged, and Resolved.");
            }

            if (IsNamed(parameter, "lowStockOnly"))
            {
                parameter.Description = Append(parameter.Description, "Set true to show only products where stock is at or below the reorder threshold.");
            }
        }
    }

    private static void ImproveOperationDescription(OpenApiOperation operation, string path, string? method)
    {
        if (path.Equals("api/products", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            AppendDescription(operation, "Demo step: execute this first to find product IDs. The Development seed also includes a stable Swagger demo product.");
        }
        else if (path.Equals("api/products", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            AppendDescription(operation, "Demo step: execute the provided request body to create a product, then copy the returned id if you want to order that exact product. The example SKU is fixed, so a second click returns 409 (duplicate SKU); change the sku to create another product.");
        }
        else if (path.Equals("api/products/{id}", StringComparison.OrdinalIgnoreCase) && method == "DELETE")
        {
            AppendDescription(operation, "Demo warning: this deactivates the product. Do not run it on the demo product, because the order example would then return 409. If that happens, reactivate it with PUT /api/products/{id} (the example body sets isActive to true).");
        }
        else if (path.Equals("api/orders", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            AppendDescription(operation, $"Demo step: the request example uses the Development seeded product {DemoProductId}, so it can be executed as-is after startup. Each execution takes 2 units of that product (50 are seeded), so it keeps working for about 25 clicks; after that it returns 409 (insufficient stock) until you restock with PATCH /api/products/{{id}}/stock. Once stock falls to the reorder threshold the sentinel raises a low-stock alert. To order a product you just created, replace productId with the id returned by POST /api/products.");
        }
        else if (path.Equals("api/orders/{id}/status", StringComparison.OrdinalIgnoreCase))
        {
            AppendDescription(operation, "Demo step: create an order first, copy the returned order id into the path, then execute the example body to move Pending -> Confirmed. A second click on the same order returns 409, because Confirmed -> Confirmed is not a valid transition.");
        }
    }

    // Route parameters keep their declared casing (id), but query-string objects are named after their properties
    // (ProductId, LowStockOnly), so names must be compared without regard to case.
    private static bool IsNamed(IOpenApiParameter parameter, string name) =>
        string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase);

    private static void AppendDescription(OpenApiOperation operation, string text) =>
        operation.Description = Append(operation.Description, text);

    private static string Append(string? existing, string addition)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return addition;
        }

        if (existing.Contains(addition, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing.Trim()}\n\n{addition}";
    }
}
