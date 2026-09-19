using System.Net;
using System.Text.Json;

namespace Fulfillment.IntegrationTests;

public class ServerErrorTests(ThrowingApiFactory factory) : IClassFixture<ThrowingApiFactory>
{
    [Fact]
    public async Task An_unhandled_exception_becomes_a_generic_500_problem()
    {
        var response = await factory.Client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.ReadAsync<JsonElement>();
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", body.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("instance").GetString()));
    }

    [Fact]
    public async Task Nothing_about_the_failure_leaks_to_the_client()
    {
        var response = await factory.Client.GetAsync($"/api/products/{Guid.NewGuid()}");

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("boom", raw);
        Assert.DoesNotContain("hunter2", raw);
        Assert.DoesNotContain("Password", raw);
        Assert.DoesNotContain("InvalidOperationException", raw);
        Assert.DoesNotContain("   at ", raw); // no stack trace
        Assert.DoesNotContain("ExplodingProductService", raw);
    }

    [Fact]
    public async Task Every_verb_that_reaches_the_failing_service_gets_the_same_treatment()
    {
        var id = Guid.NewGuid();
        var responses = new[]
        {
            await factory.Client.GetAsync("/api/products"),
            await factory.Client.PostAsync("/api/products", System.Net.Http.Json.JsonContent.Create(
                new { sku = "X-1", name = "X", unitPrice = 1, stockQuantity = 1, reorderThreshold = 0 })),
            await factory.Client.DeleteAsync($"/api/products/{id}"),
            await factory.Client.PatchAsync($"/api/products/{id}/stock", System.Net.Http.Json.JsonContent.Create(new { delta = 1 }))
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode));
        foreach (var response in responses)
        {
            Assert.DoesNotContain("boom", await response.Content.ReadAsStringAsync());
        }
    }
}
