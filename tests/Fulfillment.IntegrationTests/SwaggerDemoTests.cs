using System.Net;
using System.Text;
using System.Text.Json;
using Fulfillment.Application.Dtos;
using Fulfillment.Domain.Enums;
using Fulfillment.Infrastructure.Persistence;

namespace Fulfillment.IntegrationTests;

/// <summary>
/// Guards the "demo everything from Swagger alone" experience: the examples the document serves must exist, be
/// pre-filled where that is safe, and, most importantly, actually work when executed as they are, against an app
/// seeded exactly as a developer running it locally would have it.
/// </summary>
public class SwaggerDemoTests(DemoApiFactory demo) : IClassFixture<DemoApiFactory>
{
    private static readonly string DemoProductId = DemoSeedData.SwaggerOrderProductId.ToString();

    private HttpClient Client => demo.Client;

    private async Task<JsonElement> DocumentAsync()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<JsonElement>();
    }

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    /// <summary>The example body exactly as Swagger UI would show it in the request box.</summary>
    private static string ExampleBody(JsonElement document, string path, string method) =>
        Operation(document, path, method)
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example")
            .GetRawText();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static JsonElement? Parameter(JsonElement document, string path, string method, string name) =>
        Operation(document, path, method).TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray()
                .Where(p => string.Equals(p.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
                .Select(p => (JsonElement?)p).FirstOrDefault()
            : null;

    [Fact]
    public async Task Every_operation_that_takes_a_json_body_has_an_example()
    {
        var document = await DocumentAsync();

        var withoutExample = new List<string>();
        foreach (var path in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.TryGetProperty("requestBody", out var body) &&
                    !body.GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _))
                {
                    withoutExample.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        Assert.Empty(withoutExample);
    }

    [Theory]
    [InlineData("/api/products/{id}", "get")]
    [InlineData("/api/products/{id}", "put")]
    [InlineData("/api/products/{id}/stock", "patch")]
    public async Task Product_ids_are_prefilled_with_the_demo_product_so_they_run_in_one_click(string path, string method)
    {
        var document = await DocumentAsync();

        var id = Parameter(document, path, method, "id");

        Assert.NotNull(id);
        Assert.Equal(DemoProductId, id.Value.GetProperty("example").GetString());
    }

    [Fact]
    public async Task Delete_is_deliberately_not_prefilled_so_a_stray_click_cannot_deactivate_the_demo_product()
    {
        var document = await DocumentAsync();

        var id = Parameter(document, "/api/products/{id}", "delete", "id");

        Assert.NotNull(id);
        Assert.False(id.Value.TryGetProperty("example", out _));
    }

    [Fact]
    public async Task The_alert_product_filter_is_not_prefilled_because_that_would_hide_every_other_alert()
    {
        var document = await DocumentAsync();

        var filter = Parameter(document, "/api/alerts", "get", "productId");

        Assert.NotNull(filter);
        Assert.False(filter.Value.TryGetProperty("example", out _));
    }

    [Theory]
    [InlineData("/api/products", "get", "Demo step")]
    [InlineData("/api/products", "post", "second click returns 409")]
    [InlineData("/api/products/{id}", "delete", "Demo warning")]
    [InlineData("/api/orders", "post", "Demo step")]
    [InlineData("/api/orders", "post", "about 25 clicks")]
    [InlineData("/api/orders/{id}/status", "put", "second click on the same order returns 409")]
    // These come from the controllers' XML <remarks>. The orders row above proves both sources reach the document
    // together, which fails if the XML-comments filter is ever registered after the demo filter again.
    [InlineData("/api/orders", "post", "no stock is deducted")]
    [InlineData("/api/products/{id}/stock", "patch", "delta rather than an absolute")]
    [InlineData("/api/alerts", "get", "Demo flow")]
    [InlineData("/api/alerts/{id}/resolve", "post", "no request body")]
    public async Task Guidance_from_the_demo_filter_and_the_xml_remarks_both_reach_the_document(string path, string method, string expected)
    {
        var document = await DocumentAsync();

        var description = Operation(document, path, method).GetProperty("description").GetString();

        Assert.Contains(expected, description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/api/products", "get", "LowStockOnly", "reorder threshold")]
    [InlineData("/api/orders", "get", "Status", "Pending")]
    [InlineData("/api/alerts", "get", "Status", "Acknowledged")]
    [InlineData("/api/alerts", "get", "ProductId", "11111111-1111-1111-1111-111111111111")]
    [InlineData("/api/orders/{id}", "get", "id", "POST /api/orders")]
    [InlineData("/api/alerts/{id}/resolve", "post", "id", "GET /api/alerts")]
    public async Task Parameters_carry_their_guidance_whatever_casing_ASP_NET_gives_them(string path, string method, string name, string expected)
    {
        // Regression guard: query-object parameters are named `ProductId` / `LowStockOnly` (PascalCase) in the
        // document, and an earlier case-sensitive match silently never applied any of this text.
        var document = await DocumentAsync();

        var parameter = Parameter(document, path, method, name);

        Assert.NotNull(parameter);
        Assert.Contains(expected, parameter.Value.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_demo_product_is_seeded_exactly_once_even_though_startup_ran_more_than_once()
    {
        // The test factory starts the application twice against one database, like a developer restarting it.
        var response = await Client.GetAsync("/api/products?search=DEMO-ORDER");

        var page = await response.ReadAsync<Fulfillment.Application.Common.PagedResult<ProductResponse>>();

        var product = Assert.Single(page.Items);
        Assert.Equal(DemoProductId, product.Id.ToString());
        Assert.Equal(DemoSeedData.SwaggerOrderProductSku, product.Sku);
    }

    [Fact]
    public async Task The_served_examples_execute_successfully_exactly_as_they_are()
    {
        var document = await DocumentAsync();

        // 1. Create a product from the example.
        var create = await Client.PostAsync("/api/products", Json(ExampleBody(document, "/api/products", "post")));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // 2. Adjust the demo product's stock, and update it, with the prefilled id and the examples.
        var patch = await Client.PatchAsync(
            $"/api/products/{DemoProductId}/stock", Json(ExampleBody(document, "/api/products/{id}/stock", "patch")));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var put = await Client.PutAsync(
            $"/api/products/{DemoProductId}", Json(ExampleBody(document, "/api/products/{id}", "put")));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var stockBeforeOrder = (await put.ReadAsync<ProductResponse>()).StockQuantity;

        // 3. Place the example order. It references the seeded demo product, so it must succeed as-is and take stock.
        var orderBody = ExampleBody(document, "/api/orders", "post");
        var quantity = JsonDocument.Parse(orderBody).RootElement.GetProperty("items")[0].GetProperty("quantity").GetInt32();
        var place = await Client.PostAsync("/api/orders", Json(orderBody));
        Assert.Equal(HttpStatusCode.Created, place.StatusCode);
        var order = await place.ReadAsync<OrderResponse>();
        Assert.Equal(OrderStatus.Pending, order.Status);

        var product = await (await Client.GetAsync($"/api/products/{DemoProductId}")).ReadAsync<ProductResponse>();
        Assert.Equal(stockBeforeOrder - quantity, product.StockQuantity);

        // 4. Move that order along with the status example.
        var status = await Client.PutAsync(
            $"/api/orders/{order.Id}/status", Json(ExampleBody(document, "/api/orders/{id}/status", "put")));
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, (await status.ReadAsync<OrderResponse>()).Status);
    }

    [Fact]
    public async Task The_one_shot_examples_fail_cleanly_on_a_second_click_as_the_guidance_says()
    {
        var document = await DocumentAsync();
        var createBody = ExampleBody(document, "/api/products", "post");
        await Client.PostAsync("/api/products", Json(createBody)); // may already exist from another test; either way it exists now

        var secondClick = await Client.PostAsync("/api/products", Json(createBody));

        Assert.Equal(HttpStatusCode.Conflict, secondClick.StatusCode);
    }
}
