using Fulfillment.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Time.Testing;

namespace Fulfillment.UnitTests.Support;

/// <summary>
/// A throw-away in-memory SQLite database built by running the real migrations, so constraints, indexes
/// and concurrency tokens behave exactly as in production. Create a fresh context per "request" to mimic
/// one DI scope each.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public FulfillmentDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<FulfillmentDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options, Time);

    public void Dispose() => _connection.Dispose();
}
