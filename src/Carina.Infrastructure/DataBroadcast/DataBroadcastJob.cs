using Carina.Contracts;
using Carina.Domain.Captions;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Takes the data broadcast out of ended recordings one at a time, newest first, at the moments the captions are
/// taken, and keeps the record of it on the shelf beside them. A pass starts nothing unless the machine is idle as
/// <see cref="Idleness"/> judges it for the captions too, and reads a recording's
/// file only in its turn among the passes that read recordings through. It first puts in the queue every ended
/// recording whose record is not yet due, and, once idle, every record that failed with tries left and every
/// record the row says is made while none is kept. A recording whose file has gone keeps the record already taken
/// of it.
/// </summary>
public sealed class DataBroadcastJob(
    IServiceScopeFactory scopes,
    IDataBroadcastTaker taker,
    DataBroadcastShelf shelf,
    CaptionSettings settings,
    IntegritySettings mounts,
    RecordingReadTurn turn,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<DataBroadcastJob> logger) : BackgroundService
{
    private int running;

    public async Task<DataBroadcastPass> RunAsync(CancellationToken cancellationToken)
    {
        if (!shelf.KeepsAnything)
        {
            return DataBroadcastPass.RefusedBecauseThereIsNowhereToKeepThem();
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) is not 0)
        {
            return DataBroadcastPass.RefusedBecauseOneIsRunning();
        }

        try
        {
            return await PassAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!shelf.KeepsAnything)
        {
            logger.LogWarning("No directory is configured for captions, so no data broadcast is ever taken from a recording.");

            return;
        }

        TimeSpan waiting = settings.BeforeFirstPass;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(waiting, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waiting = settings.BetweenPasses;

            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A data broadcast pass failed; the next one is unaffected.");
            }
        }
    }

    private async Task<DataBroadcastPass> PassAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IDataBroadcastWorklist worklist = scope.ServiceProvider.GetRequiredService<IDataBroadcastWorklist>();
        IBusynessReader busyness = scope.ServiceProvider.GetRequiredService<IBusynessReader>();
        int caughtUp = await worklist.CatchUpEndedAsync(cancellationToken);

        if (await BusyAsync(busyness, cancellationToken))
        {
            return Finished(DataBroadcastPass.YieldedBeforeReadingAnything(caughtUp));
        }

        int requeued = caughtUp + await worklist.RetryFailedAsync(cancellationToken) + await RequeueLostAsync(worklist, cancellationToken);
        IReadOnlyList<OutputRoot> withinReach = [.. mounts.OutputRoots.Select(mounted => mounted.Root)];
        IReadOnlyList<DataBroadcastSubject> awaiting = await worklist.AwaitingAsync(withinReach, settings.AtMostAPass, cancellationToken);
        int outOfReach = await worklist.WaitingOutOfReachAsync(withinReach, cancellationToken);
        Tally tally = new();

        foreach (DataBroadcastSubject subject in awaiting)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await BusyAsync(busyness, cancellationToken))
            {
                tally = tally with { Yielded = true };

                break;
            }

            using IDisposable reading = await turn.TakeAsync(cancellationToken);

            if (await BusyAsync(busyness, cancellationToken))
            {
                tally = tally with { Yielded = true };

                break;
            }

            tally = tally.Counting(await TakeAsync(worklist, subject, cancellationToken));
        }

        return Finished(DataBroadcastPass.Of(
            awaiting.Count,
            tally.Made,
            tally.Missing,
            tally.Failed,
            outOfReach,
            tally.Yielded,
            requeued));
    }

    private DataBroadcastPass Finished(DataBroadcastPass pass)
    {
        if (pass.Settled > 0 || pass.Requeued > 0)
        {
            events.Signal(AppEventName.Recordings);
        }

        Told(pass);

        return pass;
    }

    private async Task<int> RequeueLostAsync(IDataBroadcastWorklist worklist, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> shelved = shelf.Shelved();
        int requeued = 0;

        foreach (RecordingId id in await worklist.MadeAsync(cancellationToken))
        {
            if (!shelved.Contains(id.Wire) && await worklist.LostAsync(id, cancellationToken))
            {
                requeued++;
            }
        }

        if (requeued > 0)
        {
            logger.LogWarning(
                "{Lost} recording(s) said the record of their data broadcast was made and none was on the shelf, so it is taken again.",
                requeued);
        }

        return requeued;
    }

    private async Task<bool> BusyAsync(IBusynessReader busyness, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        return Idleness.Judge(await busyness.ReadAsync(now, cancellationToken), now) is not IdleVerdict.Idle;
    }

    private async Task<DataBroadcastState?> TakeAsync(
        IDataBroadcastWorklist worklist,
        DataBroadcastSubject subject,
        CancellationToken cancellationToken)
    {
        try
        {
            return await TakenAsync(worklist, subject, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(
                failure,
                "Taking the data broadcast of recording {Recording} threw, which leaves the recording itself untouched "
                + "and counts as a failure.",
                subject.Id.Wire);

            return await SettledAsync(worklist.FailedAsync(subject.Id, cancellationToken), subject.Id, DataBroadcastState.Failed, false);
        }
    }

    private async Task<DataBroadcastState?> TakenAsync(
        IDataBroadcastWorklist worklist,
        DataBroadcastSubject subject,
        CancellationToken cancellationToken)
    {
        if (Mounted(subject.Root) is not { } root)
        {
            logger.LogWarning(
                "Output root {Root} is named by the ledger and nothing tells this process where it is mounted, "
                + "so recording {Recording} keeps its place in the queue.",
                subject.Root.Value,
                subject.Id.Wire);

            return null;
        }

        string source = Path.Combine(root, subject.FileName.Value);

        if (!File.Exists(source))
        {
            return HoldsAnything(root)
                ? await WithoutItsFileAsync(worklist, subject.Id, cancellationToken)
                : LostMount(subject);
        }

        DataBroadcastTaking taking = await taker.TakeAsync(source, subject.Service, cancellationToken);

        if (taking.Fault is { } fault)
        {
            logger.LogWarning(
                "The data broadcast of recording {Recording} could not be taken: {Fault}. {Note}",
                subject.Id.Wire,
                fault,
                taking.Note);

            return await SettledAsync(worklist.FailedAsync(subject.Id, cancellationToken), subject.Id, DataBroadcastState.Failed, false);
        }

        if (taking.Record is not { } record)
        {
            shelf.Forget(subject.Id);

            return await SettledAsync(worklist.TakenAsync(subject.Id, 0, cancellationToken), subject.Id, DataBroadcastState.Missing, false);
        }

        await shelf.KeepAsync(subject.Id, record, cancellationToken);

        return await SettledAsync(worklist.TakenAsync(subject.Id, record.Modules, cancellationToken), subject.Id, DataBroadcastState.Made, true);
    }

    /// <summary>
    /// A recording whose file is no longer on the disk keeps the record already taken of it, and is missing when
    /// there is none.
    /// </summary>
    private async Task<DataBroadcastState?> WithoutItsFileAsync(
        IDataBroadcastWorklist worklist,
        RecordingId id,
        CancellationToken cancellationToken)
    {
        if (await shelf.ReadAsync(id, cancellationToken) is { Modules: > 0 } kept)
        {
            logger.LogInformation(
                "The file of recording {Recording} is no longer on the disk, and the record of its data broadcast taken before is kept.",
                id.Wire);

            return await SettledAsync(worklist.TakenAsync(id, kept.Modules, cancellationToken), id, DataBroadcastState.Made, false);
        }

        return await SettledAsync(worklist.TakenAsync(id, 0, cancellationToken), id, DataBroadcastState.Missing, false);
    }

    private async Task<DataBroadcastState?> SettledAsync(Task<bool> keeping, RecordingId id, DataBroadcastState state, bool written)
    {
        if (await keeping)
        {
            return state;
        }

        logger.LogInformation(
            "Recording {Recording} went while its data broadcast was being taken, so nothing is kept for it.",
            id.Wire);

        if (written)
        {
            shelf.Forget(id);
        }

        return null;
    }

    private DataBroadcastState? LostMount(DataBroadcastSubject subject)
    {
        logger.LogWarning(
            "Output root {Root} holds nothing at all, which is what it looks like when its mount has gone, "
            + "so recording {Recording} keeps its place in the queue.",
            subject.Root.Value,
            subject.Id.Wire);

        return null;
    }

    private void Told(DataBroadcastPass pass)
    {
        if (pass.Read is 0 && pass.OutOfReach is 0 && pass.Requeued is 0)
        {
            return;
        }

        logger.LogInformation(
            "A data broadcast pass read {Read} recording(s): {Made} made, {Missing} without a data broadcast, "
            + "{Failed} failed, {OutOfReach} left unread under a root out of reach, {Requeued} put back to be taken again.",
            pass.Read,
            pass.Made,
            pass.Missing,
            pass.Failed,
            pass.OutOfReach,
            pass.Requeued);

        if (pass.Yielded)
        {
            logger.LogInformation(
                "The data broadcast pass stopped with {Left} recording(s) left, because something is being recorded, "
                + "watched or about to be recorded.",
                pass.LeftForNextTime);
        }
    }

    private static bool HoldsAnything(string root)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? Mounted(OutputRoot root)
        => mounts.OutputRoots.FirstOrDefault(candidate => candidate.Root.Equals(root))?.Path;

    private sealed record Tally(int Made = 0, int Missing = 0, int Failed = 0, bool Yielded = false)
    {
        public Tally Counting(DataBroadcastState? settled)
            => settled switch
            {
                DataBroadcastState.Made => this with { Made = Made + 1 },
                DataBroadcastState.Missing => this with { Missing = Missing + 1 },
                DataBroadcastState.Failed => this with { Failed = Failed + 1 },
                _ => this,
            };
    }
}
