using Fulfillment.Application.Services;
using Fulfillment.Domain.Entities;
using Fulfillment.Domain.Enums;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.Persistence;
using Fulfillment.Infrastructure.Persistence.Repositories;
using Fulfillment.UnitTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.UnitTests.Application;

public class AlertServiceTests
{
    private readonly TestDatabase _db = new();

    private AlertService CreateService(FulfillmentDbContext context) => new(
        new AlertRepository(context),
        new UnitOfWork(context, NullLogger<UnitOfWork>.Instance),
        _db.Time,
        NullLogger<AlertService>.Instance);

    private async Task<Guid> SeedOpenAlertAsync(int stock = 1, int threshold = 5)
    {
        await using var context = _db.CreateContext();
        var product = TestData.Product("LOW", stock, threshold);
        var alert = Alert.RaiseLowStock(product);
        context.Alerts.Add(alert);
        await context.SaveChangesAsync();
        return alert.Id;
    }

    [Fact]
    public async Task AcknowledgeAsync_moves_an_open_alert_to_acknowledged()
    {
        var id = await SeedOpenAlertAsync();

        await using var context = _db.CreateContext();
        var response = await CreateService(context).AcknowledgeAsync(id, CancellationToken.None);

        Assert.Equal(AlertStatus.Acknowledged, response.Status);
        Assert.Equal(_db.Time.GetUtcNow().UtcDateTime, response.AcknowledgedAt);
    }

    [Fact]
    public async Task AcknowledgeAsync_twice_is_a_conflict()
    {
        var id = await SeedOpenAlertAsync();
        await using (var first = _db.CreateContext())
        {
            await CreateService(first).AcknowledgeAsync(id, CancellationToken.None);
        }

        await using var second = _db.CreateContext();
        await Assert.ThrowsAsync<ConflictException>(() => CreateService(second).AcknowledgeAsync(id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_closes_an_open_or_acknowledged_alert_and_records_when(bool acknowledgeFirst)
    {
        var id = await SeedOpenAlertAsync();
        if (acknowledgeFirst)
        {
            await using var ack = _db.CreateContext();
            await CreateService(ack).AcknowledgeAsync(id, CancellationToken.None);
        }

        _db.Time.Advance(TimeSpan.FromMinutes(10));
        await using var context = _db.CreateContext();
        var response = await CreateService(context).ResolveAsync(id, CancellationToken.None);

        Assert.Equal(AlertStatus.Resolved, response.Status);
        Assert.Equal(_db.Time.GetUtcNow().UtcDateTime, response.ResolvedAt);
    }

    [Fact]
    public async Task ResolveAsync_is_allowed_while_stock_is_still_low()
    {
        // The product is at 1 unit against a threshold of 5. Resolving by hand must still work; it is the
        // sentinel's job (not this service's) to raise a fresh alert on its next scan.
        var id = await SeedOpenAlertAsync(stock: 1, threshold: 5);

        await using var context = _db.CreateContext();
        var response = await CreateService(context).ResolveAsync(id, CancellationToken.None);

        Assert.Equal(AlertStatus.Resolved, response.Status);
        Assert.True(response.CurrentStock <= response.Threshold);
    }

    [Fact]
    public async Task ResolveAsync_twice_is_a_conflict()
    {
        var id = await SeedOpenAlertAsync();
        await using (var first = _db.CreateContext())
        {
            await CreateService(first).ResolveAsync(id, CancellationToken.None);
        }

        await using var second = _db.CreateContext();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(second).ResolveAsync(id, CancellationToken.None));

        Assert.Contains("already resolved", ex.Message);
    }

    [Fact]
    public async Task Unknown_alerts_throw_NotFound()
    {
        await using var context = _db.CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AcknowledgeAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ResolveAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Acknowledging_an_alert_that_was_just_resolved_reports_what_really_happened()
    {
        var id = await SeedOpenAlertAsync();
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).ResolveAsync(id, CancellationToken.None);
        });

        await using var context = _db.CreateContext(race);
        // Exact type: an accurate, deterministic conflict, not the generic "modified by another request".
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(context).AcknowledgeAsync(id, CancellationToken.None));

        Assert.Contains("this alert is Resolved", ex.Message);
    }

    [Fact]
    public async Task Two_people_resolving_at_once_leave_one_success_and_one_accurate_conflict()
    {
        var id = await SeedOpenAlertAsync();
        var race = new RaceOnFirstSave(async () =>
        {
            await using var other = _db.CreateContext();
            await CreateService(other).ResolveAsync(id, CancellationToken.None);
        });

        await using var context = _db.CreateContext(race);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(context).ResolveAsync(id, CancellationToken.None));

        Assert.Contains("already resolved", ex.Message);
        await using var check = _db.CreateContext();
        Assert.Equal(AlertStatus.Resolved, (await check.Alerts.AsNoTracking().SingleAsync()).Status);
    }
}
