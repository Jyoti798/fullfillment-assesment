using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fulfillment.UnitTests.Support;

/// <summary>
/// Makes a concurrency race deterministic: just before the first save of the context it is attached to, runs
/// <paramref name="competitor"/> (typically another request committing to the same rows). The save that follows
/// is then guaranteed to lose the optimistic-concurrency check exactly once.
/// </summary>
internal sealed class RaceOnFirstSave(Func<Task> competitor) : SaveChangesInterceptor
{
    private int _fired;

    public int Fired => _fired;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _fired, 1) == 0)
        {
            await competitor();
        }

        return result;
    }
}
