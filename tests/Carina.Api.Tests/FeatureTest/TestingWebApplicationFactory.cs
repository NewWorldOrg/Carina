using Carina.Api.Authentication;
using Carina.Infrastructure.Configuration;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

/// <summary>
/// The host the feature tests raise the application from. Every application raised from it, or from
/// a host reshaped from it however many times, is stopped and let go of when this one is.
/// </summary>
public class TestingWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly Lock gate = new();

    private readonly List<(IHost Host, CancellationToken Stopped)> raised = [];

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
        AnonymousNetworks.Key,
    ];

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
        builder.UseSetting(AnonymousNetworks.Key, string.Empty);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        CancellationToken stopped = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped;

        lock (gate)
        {
            raised.Add((host, stopped));
        }

        return host;
    }

    /// <summary>
    /// Lets go of this host, then stops and lets go of every application raised from it that is still
    /// running. One that fails to stop does not keep the rest from being stopped, and none is stopped
    /// twice; what failed is thrown once everything has been let go of.
    /// </summary>
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

    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        base.ConfigureClient(client);

        client.DefaultRequestHeaders.Add(
            HeaderNames.Origin,
            client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }
}
