using System.Net;
using System.Net.Http.Json;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.IntegrationTests;

public class ProductsApiTests(FulfillmentApiFactory factory) : IClassFixture<FulfillmentApiFactory>
{
    private HttpClient _client => factory.Client;

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_test_host_really_uses_its_own_temporary_database()
    {
        await _client.CreateProductAsync();

        Assert.True(File.Exists(factory.DatabasePath), "configuration overrides did not reach the app");
    }

    [Fact]
    public async Task Create_returns_201_with_a_location_header_and_the_stored_product()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            sku = " kb-1 ",
            name = "Keyboard",
            unitPrice = 49.9m,
            stockQuantity = 8,
            reorderThreshold = 3
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadAsync<ProductResponse>();
        Assert.Equal("KB-1", created.Sku);
        Assert.EndsWith($"/api/products/{created.Id}", response.Headers.Location!.ToString());

        var fetched = await (await _client.GetAsync(response.Headers.Location)).ReadAsync<ProductResponse>();
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task Update_then_deactivate_round_trips()
    {
        var product = await _client.CreateProductAsync();

        var update = await _client.PutAsJsonAsync($"/api/products/{product.Id}", new
        {
            name = "Renamed",
            unitPrice = 3.5m,
            reorderThreshold = 6,
            isActive = true
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.ReadAsync<ProductResponse>();
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(6, updated.ReorderThreshold);

        var delete = await _client.DeleteAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var after = await (await _client.GetAsync($"/api/products/{product.Id}")).ReadAsync<ProductResponse>();
        Assert.False(after.IsActive);
    }

    [Fact]
    public async Task Invalid_body_returns_a_400_problem_with_per_field_errors()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            sku = "",
            name = "",
            unitPrice = -1,
            stockQuantity = -5,
            reorderThreshold = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Contains("Sku", problem.Errors.Keys);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("UnitPrice", problem.Errors.Keys);
        Assert.Contains("StockQuantity", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_price_finer_than_a_cent_is_rejected_with_a_400()
    {
        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            sku = ApiClientExtensions.NextSku("PRICE"),
            name = "Fractional",
            unitPrice = 9.999m,
            stockQuantity = 1,
            reorderThreshold = 0
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadAsync<ValidationProblemDetails>();
        Assert.Contains("UnitPrice", problem.Errors.Keys);
    }

    [Fact]
    public async Task Malformed_json_returns_400()
    {
        var response = await _client.PostAsync(
            "/api/products", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_sku_returns_409_regardless_of_case()
    {
        var sku = ApiClientExtensions.NextSku("DUP");
        await _client.CreateProductAsync(sku);

        var response = await _client.PostAsJsonAsync("/api/products", new
        {
            sku = sku.ToLowerInvariant(),
            name = "Copy",
            unitPrice = 1,
            stockQuantity = 1,
            reorderThreshold = 0
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Contains(sku, problem.Detail);
    }

    [Fact]
    public async Task Unknown_product_returns_404_problem_with_a_trace_id()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.ReadAsync<ProblemDetails>();
        Assert.Equal(404, problem.Status);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
    }

    [Fact]
    public async Task A_non_guid_id_does_not_match_a_route()
    {
        var response = await _client.GetAsync("/api/products/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stock_adjustment_adds_removes_and_refuses_to_go_negative()
    {
        var product = await _client.CreateProductAsync(stock: 5);

        var added = await (await _client.AdjustStockAsync(product.Id, 10)).ReadAsync<ProductResponse>();
        var removed = await (await _client.AdjustStockAsync(product.Id, -12)).ReadAsync<ProductResponse>();
        var tooMuch = await _client.AdjustStockAsync(product.Id, -4);
        var zero = await _client.AdjustStockAsync(product.Id, 0);

        Assert.Equal(15, added.StockQuantity);
        Assert.Equal(3, removed.StockQuantity);
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);
        Assert.Equal(3, await _client.StockOfAsync(product.Id));
    }

    [Fact]
    public async Task List_supports_search_low_stock_filter_and_paging()
    {
        var tag = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        for (var i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/products", new
            {
                sku = $"{tag}-{i}",
                name = $"{tag} item {i}",
                unitPrice = 1,
                stockQuantity = i < 2 ? 0 : 50, // two are low on stock
                reorderThreshold = 5
            });
        }

        var all = await (await _client.GetAsync($"/api/products?search={tag}&pageSize=2&page=3"))
            .ReadAsync<PagedResult<ProductResponse>>();
        Assert.Equal(5, all.TotalCount);
        Assert.Single(all.Items);

        var low = await (await _client.GetAsync($"/api/products?search={tag}&lowStockOnly=true"))
            .ReadAsync<PagedResult<ProductResponse>>();
        Assert.Equal(2, low.TotalCount);
        Assert.All(low.Items, p => Assert.True(p.IsLowStock));
    }

    [Fact]
    public async Task The_old_post_route_for_stock_adjustment_is_gone()
    {
        var product = await _client.CreateProductAsync();

        var response = await _client.PostAsJsonAsync($"/api/products/{product.Id}/stock", new { delta = 5 });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(10, await _client.StockOfAsync(product.Id));
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    public async Task Out_of_range_paging_is_rejected(string query)
    {
        var response = await _client.GetAsync($"/api/products?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_routes_return_a_problem_details_404()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
