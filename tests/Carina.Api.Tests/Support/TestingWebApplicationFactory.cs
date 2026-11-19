using Carina.Api.Authentication;
using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Support;

public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Lock gate = new();

    private readonly List<(IHost Host, CancellationToken Stopped)> raised = [];

    private int stopsAsked;

    public const string ConnectionStringKey = "ConnectionStrings:Carina";

    public const string DatabaseNoResolverIsAskedAbout =
        "Host=/carina-feature-tests-have-no-database;Port=5432;"
        + "Database=carina;Username=carina;Password=placeholder";

    public static IReadOnlyList<string> SettingsNamedHere { get; } =
    [
        ConnectionStringKey,
        DriverOptions.SocketPathKey,
        SealingKeyOptions.DirectoryKey,
        PublicOrigin.Key,
        TrustedProxies.ProxiesKey,
        TrustedProxies.NetworksKey,
    ];

    public int StopsAsked => Volatile.Read(ref stopsAsked);

    public string DriverSocketPath { get; init; } =
        Path.Combine(Path.GetTempPath(), "carina-feature-tests", "no-driver.sock");

    public string SealingKeysPath { get; init; } =
        Path.Combine(Path.GetTempPath(), "carina-feature-tests", "sealing-keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Development);
        builder.UseSetting(ConnectionStringKey, DatabaseNoResolverIsAskedAbout);
        builder.UseSetting(DriverOptions.SocketPathKey, DriverSocketPath);
        builder.UseSetting(SealingKeyOptions.DirectoryKey, SealingKeysPath);
        builder.UseSetting(PublicOrigin.Key, string.Empty);
        builder.UseSetting(TrustedProxies.ProxiesKey, string.Empty);
        builder.UseSetting(TrustedProxies.NetworksKey, string.Empty);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = new Raised(base.CreateHost(builder), () => Interlocked.Increment(ref stopsAsked));

        CancellationToken stopped = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped;

        lock (gate)
        {
            raised.Add((host, stopped));
        }

        return host;
    }

    public override async ValueTask DisposeAsync()
    {
        (IHost Host, CancellationToken Stopped)[] running;
        List<Exception> failures = [];

        lock (gate)
        {
            running = [.. raised];
            raised.Clear();
        }

        try
        {
            await base.DisposeAsync();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        foreach ((IHost host, CancellationToken stopped) in running)
        {
            failures.AddRange(await LetGoAsync(host, stopped));
        }

        GC.SuppressFinalize(this);

        if (failures.Count > 0)
        {
            throw new AggregateException("Some of the applications raised for the feature tests failed to stop.", failures);
        }
    }

    private static async Task<IReadOnlyList<Exception>> LetGoAsync(IHost host, CancellationToken stopped)
    {
        List<Exception> failures = [];

        try
        {
            if (!stopped.IsCancellationRequested)
            {
                await host.StopAsync();
            }
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        try
        {
            host.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }

        return failures;
    }

    private sealed class Raised(IHost host, Action stopAsked) : IHost, IAsyncDisposable
    {
        public IServiceProvider Services => host.Services;

        public Task StartAsync(CancellationToken cancellationToken = default) => host.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            stopAsked();

            return host.StopAsync(cancellationToken);
        }

        public void Dispose() => host.Dispose();

        public async ValueTask DisposeAsync()
        {
            if (host is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();

                return;
            }

            host.Dispose();
        }
    }

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        base.ConfigureClient(client);

        client.DefaultRequestHeaders.Add(
            HeaderNames.Origin,
            client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }
}
