using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed class EpgCollector(
    IServiceScopeFactory scopes,
    IDriverSignals signals,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<EpgCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(failure, "A collection sweep failed; the next one is unaffected.");
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
        IReadOnlyList<BroadcastStream> streams = await scope.ServiceProvider
            .GetRequiredService<IBroadcastStreamDirectory>()
            .ListAsync(stoppingToken);

        if (streams.Count == 0)
        {
            return;
        }

        RoundResult walked = await scope.ServiceProvider
            .GetRequiredService<CollectionRound>()
            .WalkAsync(streams, interruption.Token, stoppingToken);

        logger.LogInformation(
            "A sweep visited {Visited} of {Offered} stream(s); {Gathered} gave a guide and {Short} came back short.",
            walked.Visited,
            streams.Count,
            walked.Gathered,
            walked.CameBackShort);

        await scope.ServiceProvider
            .GetRequiredService<ArchiveTransfer>()
            .RunAsync(stoppingToken);
    }

    private void Stop(CancellationTokenSource interruption)
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
