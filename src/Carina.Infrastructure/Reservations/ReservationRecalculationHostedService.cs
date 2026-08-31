using System.Threading.Channels;

using Carina.Domain.Reservations;
using Carina.Infrastructure.Rules;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Reservations;

public sealed class ReservationRecalculationHostedService(
    IServiceScopeFactory scopes,
    RecalculationSettings settings,
    TimeProvider clock,
    ILogger<ReservationRecalculationHostedService> logger) : BackgroundService, IRecalculationNotice, IRecalculationPass
{
    private readonly Channel<byte> doorbell = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    private readonly HashSet<RecalculationTrigger> asked = [];
    private readonly Lock gate = new();

    private int running;
    private long cursor;

    public void Nudge(RecalculationTrigger trigger)
    {
        if (RecalculationReaches.Of(trigger) is RecalculationReach.Nothing)
        {
            return;
        }

        lock (gate)
        {
            asked.Add(trigger);
        }

        doorbell.Writer.TryWrite(0);
    }

    public Task<RecalculationPass> RunAsync(CancellationToken cancellationToken)
        => PassAsync(null, cancellationToken);

    public Task<RecalculationPass> RunAsync(RecalculationTrigger asking, CancellationToken cancellationToken)
        => PassAsync(asking, cancellationToken);

    private async Task<RecalculationPass> PassAsync(
        RecalculationTrigger? asking,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) is not 0)
        {
            if (asking is { } turnedAway)
            {
                Nudge(turnedAway);
            }

            return RecalculationPass.Refused(RecalculationRefusal.OneIsAlreadyRunning);
        }

        try
        {
            RecalculationTrigger[] answering;

            lock (gate)
            {
                if (asking is { } wanted)
                {
                    asked.Add(wanted);
                }

                answering = [.. asked.Order()];
                asked.Clear();
            }

            return answering.Length is 0
                ? RecalculationPass.Refused(RecalculationRefusal.NothingAsked)
                : await WalkedAsync(answering, RecalculationReaches.Widest(answering), cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await Waited(settings.BeforeFirstPass, stoppingToken))
        {
            return;
        }

        Nudge(RecalculationTrigger.AppStarted);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Told(await RunAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A recalculation pass failed; the next one is unaffected.");
            }

            if (!await WaitAsync(settings.BetweenReconciliations, stoppingToken))
            {
                break;
            }
        }
    }

    private async Task<RecalculationPass> WalkedAsync(
        IReadOnlyList<RecalculationTrigger> answering,
        RecalculationReach reach,
        CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        var faults = new List<RecalculationFault>();
        RuleApplicationRun? applied = null;

        if (reach is RecalculationReach.Increment or RecalculationReach.Everything)
        {
            try
            {
                RuleApplicationService applying =
                    scope.ServiceProvider.GetRequiredService<RuleApplicationService>();

                applied = reach is RecalculationReach.Everything
                    ? await applying.EverythingAsync(cancellationToken)
                    : await applying.SinceAsync(cursor, cancellationToken);

                cursor = applied.Revision;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                faults.Add(new RecalculationFault(RecalculationStage.Rules, failure.GetType().Name));

                logger.LogError(
                    failure,
                    "Reading the rules against the guide failed, so the guide is read from {Revision} again next "
                    + "time; what already stands is still settled on the tuners below.",
                    cursor);
            }
        }

        SchedulingRun? settled = null;

        try
        {
            settled = await scope.ServiceProvider
                .GetRequiredService<ReservationSchedulingService>()
                .RecalculateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            faults.Add(new RecalculationFault(RecalculationStage.Scheduling, failure.GetType().Name));

            logger.LogError(failure, "Settling the allocation failed; the next pass is unaffected.");
        }

        return RecalculationPass.Of(answering, reach, cursor, applied, settled, faults);
    }

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task<bool> rung = Rung(linked.Token);
        Task<bool> due = Waited(waiting, linked.Token);

        await Task.WhenAny(rung, due);
        await linked.CancelAsync();
        await rung;

        if (await due)
        {
            Nudge(RecalculationTrigger.PeriodicReconciliation);
        }

        doorbell.Reader.TryRead(out _);

        return !stoppingToken.IsCancellationRequested;
    }

    private async Task<bool> Rung(CancellationToken cancellationToken)
    {
        try
        {
            return await doorbell.Reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> Waited(TimeSpan waiting, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(waiting, clock, cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Told(RecalculationPass pass)
    {
        if (!pass.Ran)
        {
            return;
        }

        logger.LogInformation(
            "A recalculation pass answering {Triggers} reached {Reach} and left the guide read to {Revision}: "
            + "{Made} reservation(s) made, {Withdrawn} withdrawn, {Faults} stage(s) faulted.",
            string.Join(", ", pass.Answering),
            pass.Reach,
            pass.Revision,
            pass.Applied?.Made.Count ?? 0,
            pass.Applied?.Withdrawn.Count ?? 0,
            pass.Faults.Count);

        foreach (RecalculationFault fault in pass.Faults)
        {
            logger.LogWarning("The {Stage} stage of a recalculation pass faulted: {Fault}.", fault.Stage, fault.Fault);
        }
    }
}
