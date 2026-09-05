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

        ILogoVisitRepository visits = provider.GetRequiredService<ILogoVisitRepository>();

        if (LogoRotation.NextDue(
                streams,
                await visits.ListAsync(stoppingToken),
                settings,
                clock.GetUtcNow().UtcDateTime) is not { } due)
        {
            return;
        }

        using CancellationTokenSource walking = CancellationTokenSource.CreateLinkedTokenSource(
            interruption.Token,
            stoppingToken);

        LogoVisitResult visit = await Visited(provider, due, walking.Token, stoppingToken);

        if (visit.WorthWaitingOut)
        {
            logger.LogInformation("Every tuner stayed busy; the logo sweep waits for the next round.");

            return;
        }

        LogosWritten written = await provider
            .GetRequiredService<LogoWriter>()
            .WriteAsync(visit, stoppingToken);

        await visits.RecordAsync(
            due.NetworkId,
            due.TransportStreamId,
            visit.Outcome,
            clock.GetUtcNow().UtcDateTime,
            stoppingToken);

        logger.LogInformation(
            "A logo sweep of {NetworkId}-{TransportStreamId} ended as {Outcome} with {Pictures} picture(s)"
            + " for {Stations} station(s), and {NoPicture} station(s) that broadcast none.",
            due.NetworkId.Value,
            due.TransportStreamId.Value,
            visit.Outcome,
            written.Pictures,
            written.Stations,
            written.NoPicture);
    }

    private static async Task<LogoVisitResult> Visited(
        IServiceProvider provider,
        BroadcastStream due,
        CancellationToken walking,
        CancellationToken abort)
    {
        try
        {
            return await provider.GetRequiredService<LogoVisitor>().VisitAsync(due, walking);
        }
        catch (OperationCanceledException) when (!abort.IsCancellationRequested)
        {
            return LogoVisitResult.NothingCameOfIt(LogoVisitOutcome.Interrupted);
        }
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
