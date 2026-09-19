namespace Fulfillment.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Persists all pending changes in a single transaction. Uniqueness violations are surfaced as
    /// <see cref="Fulfillment.Domain.Exceptions.ConflictException"/> and lost optimistic-concurrency races as
    /// <see cref="Fulfillment.Domain.Exceptions.ConcurrencyConflictException"/>.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs a complete load, change, save operation. If a concurrent writer wins the race, the change tracker is
    /// cleared and the operation runs again on fresh data, a few times at most, so a request only fails on a
    /// genuine conflict (such as stock that really has run out) and not because someone else was a moment quicker.
    /// The operation must load everything it needs itself and have no side effects before its save succeeds.
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken);
}
