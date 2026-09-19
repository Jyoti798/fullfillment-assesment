using Fulfillment.Application.Dtos;
using Fulfillment.Domain.Enums;
using Fulfillment.Infrastructure.Persistence;
using Swashbuckle.AspNetCore.Filters;

namespace Fulfillment.Api.Swagger.Examples;

public sealed class CreateOrderRequestExample : IExamplesProvider<CreateOrderRequest>
{
    public CreateOrderRequest GetExamples() => new()
    {
        CustomerName = "John Doe",
        CustomerEmail = "john.doe@example.com",
        Items =
        [
            new()
            {
                ProductId = DemoSeedData.SwaggerOrderProductId,
                Quantity = 2
            }
        ]
    };
}

public sealed class UpdateOrderStatusRequestExample : IExamplesProvider<UpdateOrderStatusRequest>
{
    public UpdateOrderStatusRequest GetExamples() => new()
    {
        Status = OrderStatus.Confirmed
    };
}
