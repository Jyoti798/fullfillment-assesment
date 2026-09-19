using Fulfillment.Domain.Enums;

namespace Fulfillment.Application.Dtos;

public sealed record AlertResponse(
    Guid Id,
    Guid ProductId,
    string ProductSku,
    string ProductName,
    AlertType Type,
    AlertStatus Status,
    int StockAtDetection,
    int Threshold,
    int CurrentStock,
    string Message,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt,
    DateTime? ResolvedAt);

public sealed class AlertListQuery : PagedQuery
{
    public AlertStatus? Status { get; init; }

    public Guid? ProductId { get; init; }
}
