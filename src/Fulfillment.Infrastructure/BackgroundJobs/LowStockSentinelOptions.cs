namespace Fulfillment.Infrastructure.BackgroundJobs;

public sealed class LowStockSentinelOptions
{
    public const string SectionName = "LowStockSentinel";

    /// <summary>Set to false to run the API without the background scan.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Delay between scans, in seconds.</summary>
    public int IntervalSeconds { get; init; } = 60;
}
