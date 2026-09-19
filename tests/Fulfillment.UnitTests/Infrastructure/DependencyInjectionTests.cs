using Fulfillment.Application;
using Fulfillment.Application.Abstractions;
using Fulfillment.Application.Sentinel;
using Fulfillment.Application.Services;
using Fulfillment.Infrastructure;
using Fulfillment.Infrastructure.BackgroundJobs;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Fulfillment.UnitTests.Infrastructure;

public class DependencyInjectionTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(
                new[] { ("ConnectionStrings:Fulfillment", "Data Source=:memory:") }
                    .Concat(values)
                    .Select(v => new KeyValuePair<string, string?>(v.Item1, v.Item2))))
            .Build();

    // ValidateOnBuild checks every registration is constructible; ValidateScopes catches a singleton (such as the
    // hosted service) holding on to a scoped service (such as the DbContext).
    private static ServiceProvider Build(IConfiguration configuration, bool includeApplication)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();

        if (includeApplication)
        {
            services.AddApplication();
        }

        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void Infrastructure_can_be_registered_on_its_own()
    {
        // The DbContext needs a TimeProvider; AddInfrastructure must not rely on AddApplication having run first.
        using var provider = Build(Config(), includeApplication: false);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOrderRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAlertRepository>());
    }

    [Fact]
    public void The_full_composition_has_no_captive_or_missing_dependencies()
    {
        using var provider = Build(Config(), includeApplication: true);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProductService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOrderService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAlertService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILowStockScanner>());
    }

    [Fact]
    public void A_registered_clock_is_not_overwritten_by_either_layer()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddApplication();
        services.AddInfrastructure(Config());

        using var provider = services.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Repositories_and_unit_of_work_share_one_DbContext_per_scope()
    {
        using var provider = Build(Config(), includeApplication: true);

        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        var second = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        using var otherScope = provider.CreateScope();

        Assert.Same(first, second);
        Assert.NotSame(first, otherScope.ServiceProvider.GetRequiredService<FulfillmentDbContext>());
    }

    [Fact]
    public void The_sentinel_is_registered_by_default_and_can_be_switched_off()
    {
        using var on = Build(Config(), includeApplication: true);
        using var off = Build(Config(("LowStockSentinel:Enabled", "false")), includeApplication: true);

        Assert.Contains(on.GetServices<IHostedService>(), s => s is LowStockSentinel);
        Assert.DoesNotContain(off.GetServices<IHostedService>(), s => s is LowStockSentinel);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void A_non_positive_scan_interval_is_rejected(string seconds)
    {
        using var provider = Build(Config(("LowStockSentinel:IntervalSeconds", seconds)), includeApplication: true);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LowStockSentinelOptions>>().Value);

        Assert.Contains("IntervalSeconds", ex.Message);
    }

    [Fact]
    public void The_interval_is_read_from_configuration()
    {
        using var provider = Build(Config(("LowStockSentinel:IntervalSeconds", "5")), includeApplication: true);

        Assert.Equal(5, provider.GetRequiredService<IOptions<LowStockSentinelOptions>>().Value.IntervalSeconds);
    }

    [Fact]
    public void A_missing_connection_string_fails_fast_with_a_clear_message()
    {
        var empty = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddInfrastructure(empty));

        Assert.Contains("Fulfillment", ex.Message);
    }
}
