using Fulfillment.Application.Abstractions;
using Fulfillment.Domain.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fulfillment.Infrastructure.Persistence;

internal sealed class UnitOfWork(FulfillmentDbContext db, ILogger<UnitOfWork> logger) : IUnitOfWork
{
    private const int SqliteConstraintError = 19;

    // A request only loses a race when another writer has just committed, so each retry is guaranteed progress
    // for someone. With this many attempts, up to this many writers contending for one row all get through.
    private const int MaxAttempts = 5;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogInformation(
                    "Lost an optimistic-concurrency race; retrying on fresh data (attempt {Next} of {Max})", attempt + 1, MaxAttempts);

                // Forget the stale rows from the failed attempt so the retry reloads them.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // EF runs everything pending here as one database transaction: all of it commits, or none of it does.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Optimistic concurrency conflict while saving changes");
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqlite && BusinessConflict(sqlite) is { } message)
        {
            logger.LogWarning(ex, "Uniqueness violation while saving changes");
            throw new ConflictException(message);
        }
        // Anything else (a foreign key or CHECK failure, say) can only be a bug in this service, not something the
        // client did, so it is deliberately not translated: it surfaces as a logged 500 rather than a misleading 409.
    }

    /// <summary>The user-facing message for the two uniqueness rules clients can trip, or null for any other failure.</summary>
    private static string? BusinessConflict(SqliteException ex)
    {
        if (ex.SqliteErrorCode != SqliteConstraintError)
        {
            return null;
        }

        if (ex.Message.Contains("UNIQUE constraint failed: Products.Sku", StringComparison.Ordinal))
        {
            return "A product with this SKU already exists.";
        }

        if (ex.Message.Contains("UNIQUE constraint failed: Alerts.ProductId", StringComparison.Ordinal))
        {
            return "An unresolved alert already exists for this product.";
        }

        return null;
    }
}
