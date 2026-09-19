using Fulfillment.Infrastructure.Persistence;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Fulfillment.Api.Swagger;

public sealed class DemoOperationFilter : IOperationFilter
{
    private static readonly string DemoProductId = DemoSeedData.SwaggerOrderProductId.ToString();

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? string.Empty;
        ImproveParameters(operation, path);
        ImproveOperationDescription(operation, path, context.ApiDescription.HttpMethod);
    }

    private static void ImproveParameters(OpenApiOperation operation, string path)
    {
        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter.Name == "id" && path.StartsWith("api/products/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    $"Product ID. In Development, the seeded Swagger demo product ID is {DemoProductId}. You can also copy an ID from GET /api/products or POST /api/products.");
            }

            if (parameter.Name == "id" && path.StartsWith("api/orders/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    "Order ID. Create an order with POST /api/orders, then copy the returned id into this field.");
            }

            if (parameter.Name == "id" && path.StartsWith("api/alerts/{id}", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(
                    parameter.Description,
                    "Alert ID. Use GET /api/alerts, copy an existing alert id, then execute this operation.");
            }

            if (parameter.Name == "productId")
            {
                parameter.Description = Append(
                    parameter.Description,
                    $"Optional product filter. In Development, use {DemoProductId} for the seeded Swagger demo product.");
            }

            if (parameter.Name == "status" && path.StartsWith("api/orders", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(parameter.Description, "Optional order status filter. Valid values include Pending, Confirmed, Shipped, Delivered, and Cancelled.");
            }

            if (parameter.Name == "status" && path.StartsWith("api/alerts", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = Append(parameter.Description, "Optional alert status filter. Valid values are Open, Acknowledged, and Resolved.");
            }

            if (parameter.Name == "lowStockOnly")
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
            AppendDescription(operation, "Demo step: execute the provided request body to create a product, then copy the returned id if you want to order that exact product.");
        }
        else if (path.Equals("api/orders", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            AppendDescription(operation, $"Demo step: the request example uses the Development seeded product {DemoProductId}, so it can be executed as-is after startup. If you want to order a product you just created, replace productId with the id returned by POST /api/products.");
        }
        else if (path.Equals("api/orders/{id}/status", StringComparison.OrdinalIgnoreCase))
        {
            AppendDescription(operation, "Demo step: create an order first, copy the returned order id into the path, then execute the example body to move Pending -> Confirmed.");
        }
        else if (path.Equals("api/alerts", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            AppendDescription(operation, "Demo step: use this to find low-stock alerts raised by the LowStockSentinel. If no alert appears yet, wait for the sentinel interval or reduce product stock below its reorder threshold.");
        }
        else if (path.Equals("api/alerts/{id}/acknowledge", StringComparison.OrdinalIgnoreCase))
        {
            AppendDescription(operation, "Demo step: requires an existing open alert. Use GET /api/alerts, copy an alert id, paste it into the path, then execute.");
        }
        else if (path.Equals("api/alerts/{id}/resolve", StringComparison.OrdinalIgnoreCase))
        {
            AppendDescription(operation, "Demo step: requires an existing open or acknowledged alert. Use GET /api/alerts, copy an alert id, paste it into the path, then execute.");
        }
    }

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
