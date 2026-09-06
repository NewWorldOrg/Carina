using Carina.Domain.Channels;
using Carina.Domain.Driver;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Logos;

public sealed class LogoCollector(
    IServiceScopeFactory scopes,
    IDriverSignals signals,
    LogoSweepSettings settings,
    TimeProvider clock,
    ILogger<LogoCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Collects)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogWarning(failure, "A logo sweep failed; the next one is unaffected.");
            }

            try
            {
                await Task.Delay(settings.BetweenSweeps, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var interruption = new CancellationTokenSource();
        using IDisposable subscription = signals.Subscribe(name =>
        {
            if (string.Equals(name, DriverClientSignals.InstanceChanged, StringComparison.Ordinal))
            {
                Stop(interruption);
            }
        });

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider provider = scope.ServiceProvider;

        IReadOnlyList<BroadcastStream> streams = await provider
            .GetRequiredService<IBroadcastStreamDirectory>()
            .ListAsync(stoppingToken);
        IReadOnlyList<LogoVisit> walked = await provider
            .GetRequiredService<ILogoVisitRepository>()
            .ListAsync(stoppingToken);
        IReadOnlyList<BroadcastStream> due = LogoRotation.DueNow(
            streams,
            walked,
            settings,
            clock.GetUtcNow().UtcDateTime);

        if (due.Count is 0)
        {
            return;
        }

        LogoRoundResult round = await provider
            .GetRequiredService<LogoRound>()
            .WalkAsync(due, interruption.Token, stoppingToken);

        logger.LogInformation(
            "A logo sweep opened {Visited} of the {Due} transport(s) that were due;"
            + " {Left} wait for the next sweep.",
            round.Visited,
            due.Count,
            round.LeftForTheNextSweep);
    }

    private static void Stop(CancellationTokenSource interruption)
    {
        try
        {
            interruption.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
