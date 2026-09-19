using Fulfillment.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Infrastructure.Persistence;

internal static class QueryableExtensions
{
    /// <summary>The query must already have a deterministic ordering.</summary>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, page, pageSize, total);
    }
}
