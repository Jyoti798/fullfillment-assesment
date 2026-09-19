using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fulfillment.Application.Common;
using Fulfillment.Application.Dtos;
using Fulfillment.Domain.Enums;

namespace Fulfillment.IntegrationTests;

internal static class ApiClientExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static int _skuCounter;

    /// <summary>Unique per call, so tests sharing a database never collide on SKU.</summary>
    public static string NextSku(string prefix = "T") => $"{prefix}-{Interlocked.Increment(ref _skuCounter):D5}";

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(Json);
        return body ?? throw new InvalidOperationException($"Empty {typeof(T).Name} response (HTTP {(int)response.StatusCode}).");
    }

    public static async Task<ProductResponse> CreateProductAsync(
        this HttpClient client, string? sku = null, int stock = 10, int threshold = 2, decimal price = 10m)
    {
        var response = await client.PostAsJsonAsync("/api/products", new
        {
            sku = sku ?? NextSku(),
            name = "Test product",
            unitPrice = price,
            stockQuantity = stock,
            reorderThreshold = threshold
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProductResponse>();
    }

    public static Task<HttpResponseMessage> PlaceOrderAsync(this HttpClient client, Guid productId, int quantity) =>
        client.PostAsJsonAsync("/api/orders", new
        {
            customerName = "Ada Lovelace",
            customerEmail = "ada@example.com",
            items = new[] { new { productId, quantity } }
        });

    public static Task<HttpResponseMessage> SetStatusAsync(this HttpClient client, Guid orderId, OrderStatus status) =>
        client.PutAsJsonAsync($"/api/orders/{orderId}/status", new { status = status.ToString() });

    public static Task<HttpResponseMessage> AdjustStockAsync(this HttpClient client, Guid productId, int delta) =>
        client.PatchAsync($"/api/products/{productId}/stock", JsonContent.Create(new { delta }));

    public static Task<HttpResponseMessage> ResolveAlertAsync(this HttpClient client, Guid alertId) =>
        client.PostAsync($"/api/alerts/{alertId}/resolve", null);

    public static async Task<int> StockOfAsync(this HttpClient client, Guid productId) =>
        (await (await client.GetAsync($"/api/products/{productId}")).ReadAsync<ProductResponse>()).StockQuantity;

    public static async Task<PagedResult<AlertResponse>> AlertsForAsync(this HttpClient client, Guid productId) =>
        await (await client.GetAsync($"/api/alerts?productId={productId}")).ReadAsync<PagedResult<AlertResponse>>();
}
