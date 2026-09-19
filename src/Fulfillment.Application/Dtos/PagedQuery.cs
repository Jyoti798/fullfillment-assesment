using System.ComponentModel.DataAnnotations;

namespace Fulfillment.Application.Dtos;

public abstract class PagedQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}
