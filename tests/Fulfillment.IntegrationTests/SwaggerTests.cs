using System.Net;
using System.Text.Json;

namespace Fulfillment.IntegrationTests;

public class SwaggerTests(FulfillmentApiFactory production, DevelopmentApiFactory development)
    : IClassFixture<FulfillmentApiFactory>, IClassFixture<DevelopmentApiFactory>
{
    private async Task<JsonElement> DocumentAsync()
    {
        var response = await development.Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<JsonElement>();
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task Swagger_is_not_exposed_outside_development(string path)
    {
        var response = await production.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task Swagger_is_served_in_development(string path)
    {
        var response = await development.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/products", "get")]
    [InlineData("/api/products", "post")]
    [InlineData("/api/products/{id}", "get")]
    [InlineData("/api/products/{id}/stock", "patch")]
    [InlineData("/api/orders", "post")]
    [InlineData("/api/orders", "get")]
    [InlineData("/api/orders/{id}", "get")]
    [InlineData("/api/alerts", "get")]
    [InlineData("/api/alerts/{id}/acknowledge", "post")]
    [InlineData("/api/alerts/{id}/resolve", "post")]
    // Not in the brief's list, but needed to run the system: edit and deactivate products, move orders along.
    [InlineData("/api/products/{id}", "put")]
    [InlineData("/api/products/{id}", "delete")]
    [InlineData("/api/orders/{id}/status", "put")]
    [InlineData("/api/alerts/{id}", "get")]
    public async Task The_document_describes_every_endpoint(string path, string method)
    {
        var document = await DocumentAsync();

        Assert.True(
            document.GetProperty("paths").TryGetProperty(path, out var item) && item.TryGetProperty(method, out _),
            $"{method.ToUpperInvariant()} {path} is missing from the OpenAPI document");
    }

    [Fact]
    public async Task Stock_adjustment_is_documented_as_patch_only()
    {
        var document = await DocumentAsync();

        var stock = document.GetProperty("paths").GetProperty("/api/products/{id}/stock");

        Assert.True(stock.TryGetProperty("patch", out _));
        Assert.False(stock.TryGetProperty("post", out _));
    }

    [Theory]
    [InlineData("/api/products", "get", new[] { "200", "400" })]
    [InlineData("/api/products", "post", new[] { "201", "400", "409" })]
    [InlineData("/api/products/{id}", "get", new[] { "200", "404" })]
    [InlineData("/api/products/{id}/stock", "patch", new[] { "200", "400", "404", "409" })]
    [InlineData("/api/orders", "get", new[] { "200", "400" })]
    [InlineData("/api/orders", "post", new[] { "201", "400", "409" })]
    [InlineData("/api/orders/{id}/status", "put", new[] { "200", "400", "404", "409" })]
    [InlineData("/api/alerts", "get", new[] { "200", "400" })]
    [InlineData("/api/alerts/{id}/acknowledge", "post", new[] { "200", "404", "409" })]
    [InlineData("/api/alerts/{id}/resolve", "post", new[] { "200", "404", "409" })]
    public async Task Operations_document_their_success_and_error_responses(string path, string method, string[] codes)
    {
        var document = await DocumentAsync();

        var responses = document.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        foreach (var code in codes)
        {
            Assert.True(responses.TryGetProperty(code, out _), $"{method.ToUpperInvariant()} {path} does not document {code}");
        }
    }

    [Fact]
    public async Task Enums_are_documented_as_their_string_names()
    {
        var document = await DocumentAsync();

        var status = document.GetProperty("components").GetProperty("schemas").GetProperty("OrderStatus");

        Assert.Equal("string", status.GetProperty("type").GetString());
        var names = status.GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Pending", names);
        Assert.Contains("Cancelled", names);
    }

    [Fact]
    public async Task The_problem_schemas_are_part_of_the_contract()
    {
        var document = await DocumentAsync();

        var schemas = document.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("ProblemDetails", out _));
        Assert.True(schemas.TryGetProperty("ValidationProblemDetails", out _));
    }

    [Fact]
    public async Task Request_examples_are_ready_for_the_swagger_demo_flow()
    {
        var document = await DocumentAsync();

        var createProduct = RequestExample(document, "/api/products", "post");
        Assert.Equal("SKU-1001", createProduct.GetProperty("sku").GetString());
        Assert.Equal("Demo Product", createProduct.GetProperty("name").GetString());
        Assert.Equal("Sample product for testing", createProduct.GetProperty("description").GetString());
        Assert.Equal(100m, createProduct.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(50, createProduct.GetProperty("stockQuantity").GetInt32());
        Assert.Equal(10, createProduct.GetProperty("reorderThreshold").GetInt32());

        var createOrder = RequestExample(document, "/api/orders", "post");
        Assert.Equal("John Doe", createOrder.GetProperty("customerName").GetString());
        Assert.Equal("john.doe@example.com", createOrder.GetProperty("customerEmail").GetString());

        var item = Assert.Single(createOrder.GetProperty("items").EnumerateArray());
        Assert.Equal("11111111-1111-1111-1111-111111111111", item.GetProperty("productId").GetString());
        Assert.InRange(item.GetProperty("quantity").GetInt32(), 1, 5);

        var status = RequestExample(document, "/api/orders/{id}/status", "put");
        Assert.Equal("Confirmed", status.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/api/alerts/{id}/acknowledge")]
    [InlineData("/api/alerts/{id}/resolve")]
    public async Task Alert_mutations_document_how_to_get_the_required_alert_id(string path)
    {
        var document = await DocumentAsync();

        var description = document.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("description").GetString();

        Assert.Contains("GET /api/alerts", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("copy", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no request body", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_resolve_endpoint_documents_that_the_sentinel_may_re_raise()
    {
        var document = await DocumentAsync();

        var resolve = document.GetProperty("paths").GetProperty("/api/alerts/{id}/resolve").GetProperty("post");

        Assert.Contains("fresh alert", resolve.GetProperty("summary").GetString() + resolve.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement RequestExample(JsonElement document, string path, string method) =>
        document.GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method)
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example");
}
