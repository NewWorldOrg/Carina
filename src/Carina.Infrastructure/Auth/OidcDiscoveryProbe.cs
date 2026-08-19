using Carina.Domain.Auth;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Auth;

public sealed class OidcDiscoveryProbe(
    IServiceScopeFactory scopes,
    IOidcReachability reachability,
    TimeProvider clock,
    ILogger<OidcDiscoveryProbe> logger) : BackgroundService
{
    public static TimeSpan BetweenProbes { get; } = TimeSpan.FromMinutes(5);

    public async Task ProbeOnceAsync(
        IOidcSettingsRepository settings,
        IOidcDirectory directory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(directory);

        OidcSettings? held = await settings.FindAsync(cancellationToken);

        if (held?.IsConfigured is not true)
        {
            reachability.Record(OidcReach.NotConfigured);

            return;
        }

        if (await directory.ProbeAsync(held, cancellationToken) is null)
        {
            logger.LogWarning(
                "The identity provider's discovery document could not be read, so signing in through it is "
                + "degraded until it answers again. The local account still signs in.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var pace = new PeriodicTimer(BetweenProbes, clock);

        do
        {
            await SweepAsync(stoppingToken);
        }
        while (await WaitAsync(pace, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer pace, CancellationToken stoppingToken)
    {
        try
        {
            return await pace.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            await ProbeOnceAsync(
                scope.ServiceProvider.GetRequiredService<IOidcSettingsRepository>(),
                scope.ServiceProvider.GetRequiredService<IOidcDirectory>(),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "The identity provider could not be probed, so signing in through it stays degraded.");
        }
    }
}
