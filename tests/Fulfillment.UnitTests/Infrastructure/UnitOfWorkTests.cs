using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Infrastructure;

public class UnitOfWorkTests
{
    private readonly TestDatabase _db = new();

    private static UnitOfWork UnitOfWorkFor(FulfillmentDbContext context) =>
        new(context, NullLogger<UnitOfWork>.Instance);

    [Fact]
    public async Task ExecuteAsync_retries_after_a_concurrency_conflict_and_starts_each_attempt_with_a_clean_tracker()
    {
        await using (var seed = _db.CreateContext())
        {
            seed.Products.Add(TestData.Product());
            await seed.SaveChangesAsync();
        }

        await using var context = _db.CreateContext();
        var trackedAtStart = new List<int>();
        var attempts = 0;

        var result = await UnitOfWorkFor(context).ExecuteAsync(async () =>
        {
            attempts++;
            trackedAtStart.Add(context.ChangeTracker.Entries().Count());
            await context.Products.ToListAsync(); // tracks a row, so a tracker that was not cleared would show up next attempt
            if (attempts < 3)
            {
                throw new ConcurrencyConflictException();
            }

            return "done";
        }, CancellationToken.None);

        Assert.Equal("done", result);
        Assert.Equal(3, attempts);
        Assert.Equal([0, 0, 0], trackedAtStart);
    }

    [Fact]
    public async Task ExecuteAsync_gives_up_after_five_attempts_and_rethrows_the_conflict()
    {
        await using var context = _db.CreateContext();
        var attempts = 0;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UnitOfWorkFor(context).ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new ConcurrencyConflictException();
        }, CancellationToken.None));

        Assert.Equal(5, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_retry_a_genuine_conflict()
    {
        await using var context = _db.CreateContext();
        var attempts = 0;

        var ex = await Assert.ThrowsAsync<ConflictException>(() => UnitOfWorkFor(context).ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new ConflictException("Order cannot be fulfilled: SKU-1 (requested 3, available 1).");
        }, CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Contains("requested 3", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_does_not_retry_other_failures()
    {
        await using var context = _db.CreateContext();
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UnitOfWorkFor(context).ExecuteAsync<string>(() =>
        {
            attempts++;
            throw new InvalidOperationException("a bug");
        }, CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_runs_a_successful_operation_once()
    {
        await using var context = _db.CreateContext();
        var attempts = 0;

        var result = await UnitOfWorkFor(context).ExecuteAsync(() =>
        {
            attempts++;
            return Task.FromResult(42);
        }, CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_stops_retrying_once_the_request_is_cancelled()
    {
        await using var context = _db.CreateContext();
        using var cts = new CancellationTokenSource();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UnitOfWorkFor(context).ExecuteAsync<string>(() =>
        {
            attempts++;
            cts.Cancel();
            throw new ConcurrencyConflictException();
        }, cts.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_foreign_key_violation_is_a_bug_and_is_not_disguised_as_a_conflict()
    {
        await using var context = _db.CreateContext();
        var ghost = TestData.Product("GHOST");
        context.Attach(ghost); // Unchanged: EF believes the row exists, so it inserts nothing for the product
        context.Alerts.Add(Alert.RaiseLowStock(ghost));

        // Reporting this as a 409 would tell the client "you conflicted" when the server is at fault.
        await Assert.ThrowsAsync<DbUpdateException>(() => UnitOfWorkFor(context).SaveChangesAsync(CancellationToken.None));
    }
}
