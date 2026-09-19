using Fulfillment.Application.Sentinel;
using Fulfillment.Domain.Exceptions;
using Fulfillment.Infrastructure.BackgroundJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fulfillment.UnitTests.Infrastructure;

public class LowStockSentinelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private sealed class ScriptedScanner(Func<int, Task<LowStockScanResult>> script) : ILowStockScanner
    {
        private int _calls;
        private readonly TaskCompletionSource _threeCalls = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls => Volatile.Read(ref _calls);

        public Task ThreeCalls => _threeCalls.Task;

        public async Task<LowStockScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call >= 3)
            {
                _threeCalls.TrySetResult();
            }

            return await script(call);
        }
    }

    private sealed class CapturingLogger : ILogger<LowStockSentinel>
    {
        private readonly object _gate = new();
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_gate) { return _entries.ToList(); } }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private static LowStockSentinel CreateSentinel(ILowStockScanner scanner, ILogger<LowStockSentinel>? logger = null)
    {
        var services = new ServiceCollection().AddSingleton(scanner).BuildServiceProvider();
        return new LowStockSentinel(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new LowStockSentinelOptions { IntervalSeconds = 1 }),
            logger ?? NullLogger<LowStockSentinel>.Instance);
    }

    [Fact]
    public async Task A_lost_race_is_a_warning_but_an_unexpected_failure_is_an_error_and_both_are_survived()
    {
        var scanner = new ScriptedScanner(call => call switch
        {
            1 => throw new ConflictException("An unresolved alert already exists for this product."),
            2 => throw new InvalidOperationException("disk on fire"),
            _ => Task.FromResult(new LowStockScanResult(0, 0))
        });
        var logger = new CapturingLogger();
        var sentinel = CreateSentinel(scanner, logger);

        await sentinel.StartAsync(CancellationToken.None);
        await scanner.ThreeCalls.WaitAsync(Timeout);
        await sentinel.StopAsync(CancellationToken.None);

        var race = Assert.Single(logger.Entries, e => e.Message.Contains("lost a race"));
        Assert.Equal(LogLevel.Warning, race.Level);
        var failure = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("failed", failure.Message);
    }

    [Fact]
    public async Task Keeps_scanning_after_a_scan_throws()
    {
        var scanner = new ScriptedScanner(call => call == 1
            ? throw new InvalidOperationException("database unavailable")
            : Task.FromResult(new LowStockScanResult(0, 0)));
        var sentinel = CreateSentinel(scanner);

        await sentinel.StartAsync(CancellationToken.None);
        await scanner.ThreeCalls.WaitAsync(Timeout);
        await sentinel.StopAsync(CancellationToken.None);

        Assert.True(scanner.Calls >= 3);
        Assert.False(sentinel.ExecuteTask!.IsFaulted, "a failed scan must not take the background service down");
    }

    [Fact]
    public async Task Stops_promptly_on_shutdown()
    {
        var scanner = new ScriptedScanner(_ => Task.FromResult(new LowStockScanResult(0, 0)));
        var sentinel = CreateSentinel(scanner);

        await sentinel.StartAsync(CancellationToken.None);
        await scanner.ThreeCalls.WaitAsync(Timeout);
        await sentinel.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sentinel.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Runs_a_scan_immediately_at_startup_rather_than_after_the_first_interval()
    {
        var firstScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new ScriptedScanner(_ =>
        {
            firstScan.TrySetResult();
            return Task.FromResult(new LowStockScanResult(0, 0));
        });
        var sentinel = CreateSentinel(scanner);

        await sentinel.StartAsync(CancellationToken.None);
        // The interval is 1s; a scan arriving well inside that proves it did not wait for the first tick.
        await firstScan.Task.WaitAsync(TimeSpan.FromMilliseconds(800));
        await sentinel.StopAsync(CancellationToken.None);
    }
}
