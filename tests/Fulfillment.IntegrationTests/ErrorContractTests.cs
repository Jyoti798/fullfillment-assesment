using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Fulfillment.IntegrationTests;

/// <summary>
/// Every failure, whichever part of the pipeline produces it (MVC validation, routing, media-type checks, the
/// exception handler), must look the same to a client: an RFC 7807 problem with the same correlation fields.
/// </summary>
public class ErrorContractTests(FulfillmentApiFactory factory) : IClassFixture<FulfillmentApiFactory>
{
    private HttpClient Client => factory.Client;

    private static readonly string[] RequiredFields = ["type", "title", "traceId", "instance"];

    private async Task<(HttpResponseMessage Response, HttpStatusCode Expected)> SendAsync(string scenario)
    {
        switch (scenario)
        {
            case "model validation":
                return (await Client.PostAsJsonAsync("/api/products", new { sku = "", name = "", unitPrice = -1, stockQuantity = -1, reorderThreshold = 0 }),
                    HttpStatusCode.BadRequest);

            case "domain validation":
                return (await Client.PlaceOrderAsync(Guid.NewGuid(), 1), HttpStatusCode.BadRequest);

            case "query validation":
                return (await Client.GetAsync("/api/products?pageSize=0"), HttpStatusCode.BadRequest);

            case "malformed json":
                return (await Client.PostAsync("/api/products", new StringContent("{ not json", Encoding.UTF8, "application/json")),
                    HttpStatusCode.BadRequest);

            case "resource not found":
                return (await Client.GetAsync($"/api/products/{Guid.NewGuid()}"), HttpStatusCode.NotFound);

            case "unknown route":
                return (await Client.GetAsync("/api/does-not-exist"), HttpStatusCode.NotFound);

            case "wrong verb":
                return (await Client.DeleteAsync("/api/orders"), HttpStatusCode.MethodNotAllowed);

            case "unsupported media type":
                return (await Client.PostAsync("/api/products", new StringContent("hello", Encoding.UTF8, "text/plain")),
                    HttpStatusCode.UnsupportedMediaType);

            case "conflict":
            {
                var sku = ApiClientExtensions.NextSku("ERR");
                await Client.CreateProductAsync(sku);
                return (await Client.PostAsJsonAsync("/api/products", new { sku, name = "Copy", unitPrice = 1, stockQuantity = 1, reorderThreshold = 0 }),
                    HttpStatusCode.Conflict);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    [Theory]
    [InlineData("model validation")]
    [InlineData("domain validation")]
    [InlineData("query validation")]
    [InlineData("malformed json")]
    [InlineData("resource not found")]
    [InlineData("unknown route")]
    [InlineData("wrong verb")]
    [InlineData("unsupported media type")]
    [InlineData("conflict")]
    public async Task Every_error_looks_the_same_to_the_client(string scenario)
    {
        var (response, expected) = await SendAsync(scenario);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = document.RootElement;
        Assert.Equal((int)expected, body.GetProperty("status").GetInt32());
        foreach (var field in RequiredFields)
        {
            Assert.True(
                body.TryGetProperty(field, out var value) && !string.IsNullOrWhiteSpace(value.GetString()),
                $"'{scenario}': the problem body is missing '{field}'. Body: {body}");
        }
    }

    [Fact]
    public async Task The_trace_id_in_the_body_is_not_shared_between_requests()
    {
        var first = await Client.GetAsync($"/api/products/{Guid.NewGuid()}");
        var second = await Client.GetAsync($"/api/products/{Guid.NewGuid()}");

        var a = (await first.ReadAsync<JsonElement>()).GetProperty("traceId").GetString();
        var b = (await second.ReadAsync<JsonElement>()).GetProperty("traceId").GetString();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task The_instance_names_the_request_that_failed()
    {
        var response = await Client.GetAsync($"/api/products/{Guid.Parse("11111111-1111-1111-1111-111111111111")}");

        var body = await response.ReadAsync<JsonElement>();

        Assert.Equal("GET /api/products/11111111-1111-1111-1111-111111111111", body.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Business_rule_validation_failures_carry_per_field_errors()
    {
        var response = await Client.PlaceOrderAsync(Guid.NewGuid(), 1);

        var body = await response.ReadAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = body.GetProperty("errors");
        Assert.Contains("Unknown product", errors.GetProperty("Items")[0].GetString());
    }

    [Fact]
    public async Task Conflicts_explain_what_conflicted()
    {
        var product = await Client.CreateProductAsync(stock: 1);

        var response = await Client.PlaceOrderAsync(product.Id, 5);

        var body = await response.ReadAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("requested 5, available 1", body.GetProperty("detail").GetString());
    }
}
