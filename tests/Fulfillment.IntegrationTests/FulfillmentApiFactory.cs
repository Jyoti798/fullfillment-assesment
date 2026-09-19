using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Fulfillment.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fulfillment.IntegrationTests;

/// <summary>
/// Hosts the real application in-process against a private, temporary SQLite file, so every test class
/// gets a clean database and exercises the real migrations, EF mappings and middleware pipeline.
/// <para>
/// Requests go over a real loopback socket to Kestrel rather than through the in-memory TestServer. The
/// TestServer that ships with the net8.0 packages breaks when the tests run on a newer runtime (it does not
/// implement <c>PipeWriter.UnflushedBytes</c>, which newer System.Text.Json requires), and real Kestrel
/// keeps the tests independent of whichever runtime the machine has.
/// </para>
/// The background sentinel is switched off; tests run its scanner explicitly to stay deterministic.
/// </summary>
public class FulfillmentApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"fulfillment-tests-{Guid.NewGuid():N}.db");

    private IHost? _kestrelHost;
    private HttpClient? _client;

    public string DatabasePath => _databasePath;

    /// <summary>A client bound to the running API. Shared and thread-safe.</summary>
    public HttpClient Client
    {
        get
        {
            _ = Services; // forces the host to be created
            return _client ?? throw new InvalidOperationException("The Kestrel host was not started.");
        }
    }

    /// <summary>"Testing" by default, so Development-only features (Swagger) are off, as they would be in production.</summary>
    protected virtual string EnvironmentName => "Testing";

    /// <summary>Whether the sample catalogue and the Swagger demo product are seeded. Off by default so tests own their data.</summary>
    protected virtual bool SeedSampleData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("ConnectionStrings:Fulfillment", $"Data Source={_databasePath}");
        builder.UseSetting("Database:SeedSampleData", SeedSampleData ? "true" : "false");
        builder.UseSetting("LowStockSentinel:Enabled", "false");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Building two hosts runs Program.cs twice against the same SQLite file. Initialise the database once up
        // front so the two startup initialisations are harmless no-ops instead of racing (to create the migrations
        // table, or to insert the same seed rows twice).
        PrepareDatabase();

        // The factory insists on a TestServer host (its Services back the tests that resolve scoped
        // services directly), so build that one first...
        var testHost = builder.Build();

        // ...then swap the server for Kestrel on a free port and build a second host from the same builder.
        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));
        _kestrelHost = builder.Build();
        _kestrelHost.Start();

        var address = _kestrelHost.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Last();
        _client = new HttpClient { BaseAddress = new Uri(address) };

        testHost.Start();
        return testHost;
    }

    /// <summary>Runs the app's own <see cref="DatabaseInitializer"/> (migrations and, if enabled, the seed) exactly once.</summary>
    private void PrepareDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<FulfillmentDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));

        using var provider = services.BuildServiceProvider();
        DatabaseInitializer.InitializeAsync(provider, migrate: true, seed: SeedSampleData).GetAwaiter().GetResult();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _client?.Dispose();
            _kestrelHost?.Dispose();
        }

        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        // Pooled connections keep the file locked on Windows.
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // Best effort: a leftover temp file is harmless.
            }
        }
    }
}
