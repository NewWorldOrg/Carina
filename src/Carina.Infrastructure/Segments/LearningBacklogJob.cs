using Carina.Domain.Captions;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one look at the recordings that have ended did: how many records it put back after a start,
/// what stood in the way of spare time when it was not, how many recordings it gave whether captions
/// are shown, which recording it began to read, which reading it stopped because learning was switched
/// off, how many recordings waiting to be read it passed over because their file was out of reach, and
/// which recording is being read when it was done.
/// </summary>
public sealed record LearningBacklogLook(
    int Recovered,
    SpareTimeVerdict? Verdict,
    int Captioned,
    RecordingId? Began,
    RecordingId? Stopped,
    int OutOfReach,
    RecordingId? Reading)
{
    public bool SaysAnything => Recovered > 0 || Captioned > 0 || Began is not null || Stopped is not null;
}

/// <summary>
/// Reads the learning data out of the recordings that have ended, one at a time and the most recently
/// started first, from the head of each file to its end, and gives the recordings whose captions are ready
/// whether captions are shown in each second. It looks after
/// <see cref="LearningBacklogSettings.BeforeFirstLook"/>, then every
/// <see cref="LearningBacklogSettings.BetweenLooks"/>, or every
/// <see cref="LearningBacklogSettings.WhileReading"/> while a recording is read. The first look puts the
/// records a reading left when the process last stopped back to waiting. Nothing begins outside
/// <see cref="SpareTime"/>, which is judged again before each recording; a recording begun is read to its
/// end even when recording or watching starts meanwhile, but switching learning off stops it within a
/// look, keeping what it wrote and putting its record back to waiting. A recording whose file is out of
/// reach is passed over and keeps waiting, and one whose row or file goes while it is read is read no
/// further and ends partway. Stopping the process stops the reading and leaves its record for the next
/// start. No recording's file is removed.
/// </summary>
public sealed class LearningBacklogJob(
    IServiceScopeFactory scopes,
    ILearningFollower follower,
    LearningRecords records,
    ICaptionRecords captions,
    IntegritySettings mounts,
    LearningBacklogSettings settings,
    TimeProvider clock,
    ILogger<LearningBacklogJob> logger) : BackgroundService
{
    public const string LeftUnsettled = "the reading ended without settling its record";

    private Run? run;

    private bool recovered;

    public RecordingId? Reading => run?.Recording.Id;

    public async Task<LearningBacklogLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        int putBack = recovered ? 0 : await RecoverAsync(cancellationToken);

        recovered = true;

        await ReapAsync(cancellationToken);

        if (run is { } running)
        {
            return await WatchAsync(scope, running, putBack, cancellationToken);
        }

        DateTime now = Now();
        Occupancy occupancy = await scope.ServiceProvider.GetRequiredService<IOccupancyReader>().ReadAsync(now, cancellationToken);
        SpareTimeVerdict verdict = SpareTime.Judge(occupancy, now);

        if (verdict is not SpareTimeVerdict.Spare)
        {
            return new LearningBacklogLook(putBack, verdict, 0, null, null, 0, null);
        }

        ILearningBacklog backlog = scope.ServiceProvider.GetRequiredService<ILearningBacklog>();
        int captioned = await CaptionEachAsync(backlog, cancellationToken);
        (RecordingId? began, int outOfReach) = await BeginNextAsync(scope, backlog, cancellationToken);

        return new LearningBacklogLook(putBack, verdict, captioned, began, null, outOfReach, Reading);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstLook;

        try
        {
            while (await WaitAsync(waiting, stoppingToken))
            {
                await LookOnceAsync(stoppingToken);
                waiting = run is null ? settings.BetweenLooks : settings.WhileReading;
            }
        }
        finally
        {
            await LetGoAsync();
        }
    }

    private async Task LookOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            Told(await LookAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A look for recordings to read for their learning data failed; the next one is unaffected.");
        }
    }

    private async Task<int> RecoverAsync(CancellationToken cancellationToken)
    {
        int putBack = 0;

        foreach (LearningExtraction left in await records.ListAsync(LearningExtractionState.Reading, LearningFollowJob.MostRecoveredAtOnce, cancellationToken))
        {
            bool putBackHere = await ChangedAsync(
                left.RecordingId,
                found => found.Recover(false, ExtractionVersion.Current, Now()),
                cancellationToken);

            putBack += putBackHere ? 1 : 0;
        }

        return putBack;
    }

    private async Task<LearningBacklogLook> WatchAsync(
        AsyncServiceScope scope,
        Run running,
        int putBack,
        CancellationToken cancellationToken)
    {
        RecordingId id = running.Recording.Id;

        if (!await scope.ServiceProvider.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken))
        {
            await StopReadingAsync(running, cancellationToken);

            return new LearningBacklogLook(putBack, SpareTimeVerdict.LearningOff, 0, null, id, 0, null);
        }

        RecordingStanding? standing = await scope.ServiceProvider.GetRequiredService<ILearningWorklist>().StandingAsync(id, cancellationToken);

        if (standing is null || !File.Exists(running.Recording.Source))
        {
            running.Recording.Went();
        }

        return new LearningBacklogLook(putBack, null, 0, null, null, 0, id);
    }

    private async Task<int> CaptionEachAsync(ILearningBacklog backlog, CancellationToken cancellationToken)
    {
        int captioned = 0;

        foreach (LearningExtraction record in await backlog.UncaptionedAsync(settings.AtMostALook, cancellationToken))
        {
            if (await captions.ReadAsync(record.RecordingId, cancellationToken) is not { } taken)
            {
                continue;
            }

            await records.KeepPartsAsync(
                record.RecordingId,
                [.. CaptionPresence.Parts(taken, record.ReadThrough).OrderByDescending(part => part.Index)],
                ExtractionVersion.Current,
                Now(),
                cancellationToken);
            captioned++;
        }

        return captioned;
    }

    private async Task<(RecordingId? Began, int OutOfReach)> BeginNextAsync(
        AsyncServiceScope scope,
        ILearningBacklog backlog,
        CancellationToken cancellationToken)
    {
        ILearningWorklist worklist = scope.ServiceProvider.GetRequiredService<ILearningWorklist>();
        IReadOnlyList<BackloggedRecording> awaiting = await backlog.AwaitingAsync(
            [.. mounts.OutputRoots.Select(mounted => mounted.Root)],
            settings.AtMostALook,
            cancellationToken);
        int outOfReach = 0;

        foreach (BackloggedRecording next in awaiting)
        {
            if (Source(next.Recording) is not { } source || !File.Exists(source))
            {
                outOfReach++;

                continue;
            }

            ExtractionChange claimed = await ClaimAsync(worklist, next, cancellationToken);

            if (claimed is ExtractionChange.Written)
            {
                Begin(next.Recording, source);

                return (next.Recording.Id, outOfReach);
            }

            if (claimed is ExtractionChange.AnotherIsReading)
            {
                break;
            }
        }

        return (null, outOfReach);
    }

    private async Task<ExtractionChange> ClaimAsync(
        ILearningWorklist worklist,
        BackloggedRecording next,
        CancellationToken cancellationToken)
    {
        Recording recording = next.Recording;

        if (next.Record is null)
        {
            ProgrammeCopy copy = ProgrammeCopy.Of(recording, await worklist.ProgrammeEndsAtAsync(recording, cancellationToken));

            await records.AddAsync(LearningExtraction.Waiting(recording.Id, copy, Now()), cancellationToken);
        }

        return await records.ChangeAsync(recording.Id, record => Claimed(record, recording), cancellationToken);
    }

    private bool Claimed(LearningExtraction record, Recording recording)
    {
        if (!record.AwaitsReading(ExtractionVersion.Current, recording.StoppedAtActual))
        {
            return false;
        }

        record.ReadAwaited(ExtractionVersion.Current, recording.StoppedAtActual, Now());

        return true;
    }

    private void Begin(Recording recording, string source)
    {
        FollowedRecording read = FollowedRecording.Recorded(recording.Id, source, recording.ServiceId, ExtractionVersion.Current);
        CancellationTokenSource stopping = new();
        Task running = Task.Run(() => follower.FollowAsync(read, stopping.Token), CancellationToken.None);

        run = new Run(read, stopping, running);
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        if (run is not { Task.IsCompleted: true } ended)
        {
            return;
        }

        run = null;
        ended.Stopping.Dispose();

        if (ended.Task.Exception is { } failure)
        {
            logger.LogError(failure, "Reading recording {Recording} for its learning data failed.", ended.Recording.Id.Wire);
        }

        bool unsettled = await ChangedAsync(
            ended.Recording.Id,
            found => found.Fail(ExtractionFailure.Other, LeftUnsettled, Now()),
            cancellationToken);

        if (unsettled)
        {
            logger.LogWarning("Reading recording {Recording} ended and left its record reading, so it is taken as failed.", ended.Recording.Id.Wire);
        }
    }

    private async Task StopReadingAsync(Run running, CancellationToken cancellationToken)
    {
        run = null;

        await running.Stopping.CancelAsync();
        await EndedAsync(running);
        running.Stopping.Dispose();
        await ChangedAsync(running.Recording.Id, found => found.Pause(Now()), cancellationToken);

        logger.LogInformation(
            "Learning was switched off, so the reading of recording {Recording} stopped; what it kept stays, and its record waits to be read.",
            running.Recording.Id.Wire);
    }

    private async Task LetGoAsync()
    {
        if (run is not { } running)
        {
            return;
        }

        run = null;

        await running.Stopping.CancelAsync();
        await EndedAsync(running);
        running.Stopping.Dispose();
    }

    private async Task<bool> ChangedAsync(RecordingId id, Action<LearningExtraction> change, CancellationToken cancellationToken)
    {
        ExtractionChange changed = await records.ChangeAsync(
            id,
            record =>
            {
                if (record.State is not LearningExtractionState.Reading)
                {
                    return false;
                }

                change(record);

                return true;
            },
            cancellationToken);

        return changed is ExtractionChange.Written;
    }

    private static async Task EndedAsync(Run running)
        => await running.Task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    private string? Source(Recording recording)
        => mounts.OutputRoots.FirstOrDefault(mounted => mounted.Root.Equals(recording.OutputRoot)) is { } mounted
            ? Path.Combine(mounted.Path, recording.FileName.Value)
            : null;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(waiting, clock, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return true;
    }

    private void Told(LearningBacklogLook look)
    {
        if (!look.SaysAnything)
        {
            return;
        }

        logger.LogInformation(
            "A look for recordings to read for their learning data put back {Recovered} record(s) left reading, gave {Captioned} "
            + "recording(s) whether captions are shown, began reading {Began}, stopped reading {Stopped}, passed over {OutOfReach} "
            + "recording(s) out of reach, and leaves {Reading} being read.",
            look.Recovered,
            look.Captioned,
            look.Began?.Wire ?? "none",
            look.Stopped?.Wire ?? "none",
            look.OutOfReach,
            look.Reading?.Wire ?? "none");
    }

    private sealed record Run(FollowedRecording Recording, CancellationTokenSource Stopping, Task Task);
}
