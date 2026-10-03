using System.Threading.Channels;

using Carina.Contracts;
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
        Channel<bool> tunersMoved = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        using IDisposable listening = signals.Subscribe(name =>
        {
            if (string.Equals(name, DriverEvents.Tuners, StringComparison.Ordinal))
            {
                tunersMoved.Writer.TryWrite(true);
            }
        });

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
                await RestAsync(tunersMoved.Reader, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Waits out the time between sweeps, or less when a tuner comes free while a visit is due. Neither
    /// an early sweep nor a look at whether one is worth starting comes sooner than
    /// <see cref="CollectionSettings.SoonestAfterASweep"/> after the last.
    /// </summary>
    private async Task RestAsync(ChannelReader<bool> tunersMoved, CancellationToken stoppingToken)
    {
        using CancellationTokenSource rested = new(settings.BetweenSweeps, clock);
        using CancellationTokenSource resting = CancellationTokenSource.CreateLinkedTokenSource(
            rested.Token,
            stoppingToken);

        try
        {
            do
            {
                await Task.Delay(settings.SoonestAfterASweep, clock, resting.Token);
                await tunersMoved.ReadAsync(resting.Token);
            }
            while (!await ThereIsAnOpeningAsync(resting.Token));

            logger.LogInformation("A tuner came free while a visit was due; the next sweep starts ahead of its time.");
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<bool> ThereIsAnOpeningAsync(CancellationToken resting)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();

            return await scope.ServiceProvider.GetRequiredService<WalkOpeningLook>().IsThereAsync(resting);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "Looking for an opening to sweep early failed; the sweep keeps to its time.");

            return false;
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource interruption = new();
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
            "A sweep visited {Visited} of {Offered} stream(s); {Gathered} gave a guide, {Short} came back short "
            + "and {TurnedAway} found every tuner busy.",
            walked.Visited,
            streams.Count,
            walked.Gathered,
            walked.CameBackShort,
            walked.TurnedAway);

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
