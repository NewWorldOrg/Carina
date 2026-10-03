using Carina.Contracts;
using Carina.Domain.Captions;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Captions;

/// <summary>
/// Takes the captions out of ended recordings one at a time, newest first, and keeps them on the shelf.
/// A pass does not start the next recording while anything is being recorded or watched, and first puts
/// back in the queue any recording whose row says its captions are ready while no record of them is kept.
/// With room left in the pass, it takes again the captions of ready recordings whose record was kept before
/// their text was taken, leaving them ready meanwhile.
/// </summary>
public sealed class CaptionJob(
    IServiceScopeFactory scopes,
    ICaptionTranscriber transcriber,
    CaptionShelf shelf,
    CaptionSettings settings,
    IntegritySettings mounts,
    IWatching watching,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<CaptionJob> logger) : BackgroundService
{
    private int running;

    public async Task<CaptionPass> RunAsync(CancellationToken cancellationToken)
    {
        if (!settings.KeepsAnything)
        {
            return CaptionPass.RefusedBecauseThereIsNowhereToKeepThem();
        }

        if (Interlocked.CompareExchange(ref running, 1, 0) is not 0)
        {
            return CaptionPass.RefusedBecauseOneIsRunning();
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
        if (!settings.KeepsAnything)
        {
            logger.LogWarning("No directory is configured for captions, so none is ever taken from a recording.");

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
                logger.LogError(failure, "A caption pass failed; the next one is unaffected.");
            }
        }
    }

    private async Task<CaptionPass> PassAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ICaptionWorklist worklist = scope.ServiceProvider.GetRequiredService<ICaptionWorklist>();
        Requeued requeued = await RequeueLostAsync(worklist, cancellationToken);
        IReadOnlyList<OutputRoot> withinReach = [.. mounts.OutputRoots.Select(mounted => mounted.Root)];
        IReadOnlyList<CaptionSubject> awaiting =
            await worklist.AwaitingAsync(withinReach, settings.AtMostAPass, cancellationToken);
        int outOfReach = await worklist.WaitingOutOfReachAsync(withinReach, cancellationToken);

        Tally tally = await TakeEachAsync(worklist, awaiting, new Tally(), cancellationToken);
        IReadOnlyList<CaptionSubject> again = tally.Yielded || awaiting.Count >= settings.AtMostAPass
            ? []
            : await TextlessAsync(worklist, requeued.StillReady, withinReach, settings.AtMostAPass - awaiting.Count, cancellationToken);

        tally = await TakeEachAsync(worklist, again, tally, cancellationToken);

        CaptionPass pass = CaptionPass.Of(
            awaiting.Count + again.Count,
            tally.Kept,
            tally.Absent,
            tally.Failed,
            outOfReach,
            tally.Yielded,
            requeued.Count,
            again.Count);

        if (pass.Settled > 0 || pass.Requeued > 0)
        {
            events.Signal(AppEventName.Recordings);
        }

        Told(pass);

        return pass;
    }

    private async Task<Tally> TakeEachAsync(
        ICaptionWorklist worklist,
        IReadOnlyList<CaptionSubject> subjects,
        Tally tally,
        CancellationToken cancellationToken)
    {
        if (tally.Yielded)
        {
            return tally;
        }

        foreach (CaptionSubject subject in subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await BusyAsync(worklist, cancellationToken))
            {
                return tally with { Yielded = true };
            }

            tally = tally.Counting(await TakeAsync(worklist, subject, cancellationToken));
        }

        return tally;
    }

    /// <summary>
    /// The recordings whose captions are ready and whose record on the shelf was kept before the text of
    /// captions was taken, so that they are taken again while they stay ready.
    /// </summary>
    private async Task<IReadOnlyList<CaptionSubject>> TextlessAsync(
        ICaptionWorklist worklist,
        IReadOnlyList<RecordingId> ready,
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> textless = shelf.Textless();
        RecordingId[] among = [.. ready.Where(id => textless.Contains(id.Wire))];

        return among.Length is 0 ? [] : await worklist.ReadyAmongAsync(among, withinReach, atMost, cancellationToken);
    }

    private async Task<Requeued> RequeueLostAsync(ICaptionWorklist worklist, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> shelved = shelf.Shelved();
        int requeued = 0;
        List<RecordingId> stillReady = [];

        foreach (RecordingId id in await worklist.ReadyAsync(cancellationToken))
        {
            if (shelved.Contains(id.Wire))
            {
                stillReady.Add(id);
            }
            else if (await worklist.CaptionAsync(id, CaptionState.Pending, null, cancellationToken))
            {
                requeued++;
            }
        }

        if (requeued > 0)
        {
            logger.LogWarning(
                "{Lost} recording(s) said their captions were ready and no record of them was on the shelf, so they are taken again.",
                requeued);
        }

        return new Requeued(requeued, stillReady);
    }

    private async Task<bool> BusyAsync(ICaptionWorklist worklist, CancellationToken cancellationToken)
        => watching.Anyone || await worklist.AnyBeingRecordedAsync(cancellationToken);

    private void Told(CaptionPass pass)
    {
        if (pass.Read is 0 && pass.OutOfReach is 0 && pass.Requeued is 0)
        {
            return;
        }

        logger.LogInformation(
            "A caption pass read {Read} recording(s): {Kept} kept, {Absent} without captions, {Failed} failed, "
            + "{OutOfReach} left unread under a root out of reach, {Requeued} put back to be taken again, "
            + "{Retaken} of those read taken again for their text.",
            pass.Read,
            pass.Kept,
            pass.Absent,
            pass.Failed,
            pass.OutOfReach,
            pass.Requeued,
            pass.Retaken);

        if (pass.Yielded)
        {
            logger.LogInformation(
                "The caption pass stopped with {Left} recording(s) left, because something is being recorded or watched.",
                pass.LeftForNextTime);
        }
    }

    private async Task<CaptionState?> TakeAsync(
        ICaptionWorklist worklist,
        CaptionSubject subject,
        CancellationToken cancellationToken)
    {
        try
        {
            return await TranscribedAsync(worklist, subject, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogWarning(
                failure,
                "Taking the captions of recording {Recording} threw, which leaves the recording itself untouched "
                + "and counts as a failure.",
                subject.Id.Wire);

            return await FailedAsync(worklist, subject.Id, cancellationToken);
        }
    }

    private Task<CaptionState?> FailedAsync(
        ICaptionWorklist worklist,
        RecordingId id,
        CancellationToken cancellationToken)
        => SettledAsync(worklist, id, CaptionState.Failed, null, cancellationToken);

    private async Task<CaptionState?> SettledAsync(
        ICaptionWorklist worklist,
        RecordingId id,
        CaptionState state,
        int? pictures,
        CancellationToken cancellationToken)
    {
        if (await worklist.CaptionAsync(id, state, pictures, cancellationToken))
        {
            return state;
        }

        logger.LogInformation(
            "Recording {Recording} went while its captions were being taken, so nothing is kept for it.",
            id.Wire);

        shelf.Forget(id);

        return null;
    }

    private async Task<CaptionState?> TranscribedAsync(
        ICaptionWorklist worklist,
        CaptionSubject subject,
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
                ? await AbsentAsync(worklist, subject.Id, cancellationToken)
                : LostMount(subject);
        }

        CaptionTranscription transcription = await transcriber.TranscribeAsync(source, subject.Service, cancellationToken);

        if (transcription.Fault is { } fault)
        {
            logger.LogWarning(
                "The captions of recording {Recording} could not be taken: {Fault}. {Note}",
                subject.Id.Wire,
                fault,
                transcription.Note);

            return await FailedAsync(worklist, subject.Id, cancellationToken);
        }

        if (transcription.Record is not { } record)
        {
            return await AbsentAsync(worklist, subject.Id, cancellationToken);
        }

        await shelf.KeepAsync(subject.Id, record, cancellationToken);

        return await SettledAsync(worklist, subject.Id, CaptionState.Ready, record.Pictures, cancellationToken);
    }

    private async Task<CaptionState?> AbsentAsync(
        ICaptionWorklist worklist,
        RecordingId id,
        CancellationToken cancellationToken)
    {
        shelf.Forget(id);

        return await SettledAsync(worklist, id, CaptionState.Absent, null, cancellationToken);
    }

    private CaptionState? LostMount(CaptionSubject subject)
    {
        logger.LogWarning(
            "Output root {Root} holds nothing at all, which is what it looks like when its mount has gone, "
            + "so recording {Recording} keeps its place in the queue.",
            subject.Root.Value,
            subject.Id.Wire);

        return null;
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

    private sealed record Requeued(int Count, IReadOnlyList<RecordingId> StillReady);

    private sealed record Tally(int Kept = 0, int Absent = 0, int Failed = 0, bool Yielded = false)
    {
        public Tally Counting(CaptionState? settled)
            => settled switch
            {
                CaptionState.Ready => this with { Kept = Kept + 1 },
                CaptionState.Absent => this with { Absent = Absent + 1 },
                CaptionState.Failed => this with { Failed = Failed + 1 },
                _ => this,
            };
    }
}
