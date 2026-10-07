using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one look at the recordings being written did: how many records it put back after a start,
/// how many follows it began and how many it stopped because learning was switched off, how many
/// records it made waiting, and how many follows were running when it was done.
/// </summary>
public sealed record LearningLook(int Recovered, int Began, int Stopped, int Waiting, int Following)
{
    public bool SaysAnything => Recovered > 0 || Began > 0 || Stopped > 0 || Waiting > 0;
}

/// <summary>
/// Follows every recording being written under an output root this process can read, one follow
/// each, for its learning data. It looks after <see cref="LearningFollowSettings.BeforeFirstLook"/>,
/// then every <see cref="LearningFollowSettings.BetweenLooks"/> and whenever the driver tells of a
/// recording's progress. The first look puts back the records a follow or a reading left running when
/// the process last stopped: a recording still being written is followed again from its head, and
/// one that has ended or gone waits to be read. While learning is off nothing is followed: a
/// recording being written gets a waiting record with the copy of its programme, and the follows
/// running when learning was switched off are stopped, their chunks kept and their records put back
/// to waiting. While learning is on, a recording being written whose file is there is followed from
/// its head, its record made following, and a follow is told when its recording ends or goes. Stopping
/// the process stops every follow and leaves its record for the next start.
/// </summary>
public sealed class LearningFollowJob(
    IServiceScopeFactory scopes,
    ILearningFollower follower,
    LearningRecords records,
    IntegritySettings mounts,
    LearningFollowSettings settings,
    IDriverSignals signals,
    TimeProvider clock,
    ILogger<LearningFollowJob> logger) : BackgroundService
{
    public const int MostRecoveredAtOnce = 1000;

    private readonly Dictionary<RecordingId, Follow> follows = [];

    private CancellationTokenSource? waking;

    private bool recovered;

    public IReadOnlyCollection<RecordingId> Following => [.. follows.Keys];

    public static bool WakesOn(string name) => string.Equals(name, DriverEvents.RecordingProgress, StringComparison.Ordinal);

    public async Task<LearningLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ILearningWorklist worklist = scope.ServiceProvider.GetRequiredService<ILearningWorklist>();
        int putBack = recovered ? 0 : await RecoverAsync(worklist, cancellationToken);

        recovered = true;

        bool learning = await scope.ServiceProvider.GetRequiredService<ILearningSwitch>().IsOnAsync(cancellationToken);
        IReadOnlyList<Recording> recording = await worklist.BeingRecordedAsync(
            [.. mounts.OutputRoots.Select(mounted => mounted.Root)],
            cancellationToken);

        Reap();
        await TellAsync(worklist, recording, cancellationToken);

        (int began, int stopped, int waiting) = learning
            ? (await FollowEachAsync(worklist, recording, cancellationToken), 0, 0)
            : await HoldEachAsync(worklist, recording, cancellationToken);

        return new LearningLook(putBack, began, stopped, waiting, follows.Count);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IDisposable subscription = signals.Subscribe(Woken);
        TimeSpan waiting = settings.BeforeFirstLook;

        try
        {
            while (await WaitAsync(waiting, stoppingToken))
            {
                waiting = settings.BetweenLooks;
                await LookOnceAsync(stoppingToken);
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
            logger.LogError(failure, "A look for recordings to follow for their learning data failed; the next one is unaffected.");
        }
    }

    private async Task<int> RecoverAsync(ILearningWorklist worklist, CancellationToken cancellationToken)
    {
        int putBack = 0;

        foreach (LearningExtractionState state in new[] { LearningExtractionState.Following, LearningExtractionState.Reading })
        {
            foreach (LearningExtraction left in await records.ListAsync(state, MostRecoveredAtOnce, cancellationToken))
            {
                RecordingStanding? standing = await worklist.StandingAsync(left.RecordingId, cancellationToken);

                if (standing is RecordingStanding.InFlight && state is LearningExtractionState.Following)
                {
                    continue;
                }

                ExtractionChange change = await records.ChangeAsync(
                    left.RecordingId,
                    record => Recovered(record, state, standing is RecordingStanding.InFlight),
                    cancellationToken);

                putBack += change is ExtractionChange.Written ? 1 : 0;
            }
        }

        return putBack;
    }

    private bool Recovered(LearningExtraction record, LearningExtractionState state, bool stillRecording)
    {
        if (record.State != state)
        {
            return false;
        }

        record.Recover(stillRecording, ExtractionVersion.Current, Now());

        return true;
    }

    private async Task<int> FollowEachAsync(
        ILearningWorklist worklist,
        IReadOnlyList<Recording> recording,
        CancellationToken cancellationToken)
    {
        int began = 0;

        foreach (Recording being in recording)
        {
            if (follows.ContainsKey(being.Id) || Source(being) is not { } source || !File.Exists(source))
            {
                continue;
            }

            if (await FollowedFromTheHeadAsync(worklist, being, cancellationToken))
            {
                Begin(being, source);
                began++;
            }
        }

        return began;
    }

    private async Task<bool> FollowedFromTheHeadAsync(ILearningWorklist worklist, Recording recording, CancellationToken cancellationToken)
    {
        LearningExtraction? record = await records.FindAsync(recording.Id, cancellationToken);
        DateTime now = Now();

        switch (record?.State)
        {
            case null:
                await records.AddAsync(
                    LearningExtraction.Following(recording.Id, await CopyAsync(worklist, recording, cancellationToken), ExtractionVersion.Current, now),
                    cancellationToken);

                return true;
            case LearningExtractionState.Waiting:
                return await ChangedAsync(recording.Id, LearningExtractionState.Waiting, found => found.Follow(ExtractionVersion.Current, now), cancellationToken);
            case LearningExtractionState.Following:
                return await ChangedAsync(recording.Id, LearningExtractionState.Following, found => found.Recover(true, ExtractionVersion.Current, now), cancellationToken);
            default:
                return false;
        }
    }

    private async Task<(int Began, int Stopped, int Waiting)> HoldEachAsync(
        ILearningWorklist worklist,
        IReadOnlyList<Recording> recording,
        CancellationToken cancellationToken)
    {
        int stopped = await StopEachAsync(cancellationToken);
        int waiting = 0;

        foreach (Recording being in recording)
        {
            LearningExtraction? record = await records.FindAsync(being.Id, cancellationToken);

            if (record is null)
            {
                await records.AddAsync(LearningExtraction.Waiting(being.Id, await CopyAsync(worklist, being, cancellationToken), Now()), cancellationToken);
                waiting++;
            }
            else if (record.State is LearningExtractionState.Following
                     && await ChangedAsync(being.Id, LearningExtractionState.Following, found => found.Pause(Now()), cancellationToken))
            {
                stopped++;
            }
        }

        return (0, stopped, waiting);
    }

    private async Task<int> StopEachAsync(CancellationToken cancellationToken)
    {
        Follow[] stopping = [.. follows.Values];

        follows.Clear();

        foreach (Follow follow in stopping)
        {
            await follow.Stopping.CancelAsync();
        }

        foreach (Follow follow in stopping)
        {
            await EndedAsync(follow);
            follow.Stopping.Dispose();
            await ChangedAsync(follow.Recording.Id, LearningExtractionState.Following, found => found.Pause(Now()), cancellationToken);
        }

        if (stopping.Length > 0)
        {
            logger.LogInformation(
                "Learning was switched off, so {Stopped} follow(s) stopped; what they kept stays, and their records wait to be read.",
                stopping.Length);
        }

        return stopping.Length;
    }

    private async Task<bool> ChangedAsync(
        RecordingId id,
        LearningExtractionState from,
        Action<LearningExtraction> change,
        CancellationToken cancellationToken)
    {
        ExtractionChange changed = await records.ChangeAsync(
            id,
            record =>
            {
                if (record.State != from)
                {
                    return false;
                }

                change(record);

                return true;
            },
            cancellationToken);

        return changed is ExtractionChange.Written;
    }

    private async Task TellAsync(ILearningWorklist worklist, IReadOnlyList<Recording> recording, CancellationToken cancellationToken)
    {
        HashSet<RecordingId> stillRecording = [.. recording.Select(being => being.Id)];

        foreach (Follow follow in follows.Values.Where(follow => !stillRecording.Contains(follow.Recording.Id) && !follow.Recording.HasGone))
        {
            RecordingStanding? standing = await worklist.StandingAsync(follow.Recording.Id, cancellationToken);

            if (standing is null)
            {
                follow.Recording.Went();
            }
            else
            {
                follow.Recording.Ended();
            }
        }
    }

    private void Begin(Recording recording, string source)
    {
        FollowedRecording followed = new(recording.Id, source, recording.ServiceId, ExtractionVersion.Current);
        CancellationTokenSource stopping = new();
        Task running = Task.Run(() => follower.FollowAsync(followed, stopping.Token), CancellationToken.None);

        follows[recording.Id] = new Follow(followed, stopping, running);
    }

    private void Reap()
    {
        foreach (Follow follow in follows.Values.Where(follow => follow.Running.IsCompleted).ToArray())
        {
            follows.Remove(follow.Recording.Id);
            follow.Stopping.Dispose();

            if (follow.Running.Exception is { } failure)
            {
                logger.LogError(failure, "Following recording {Recording} for its learning data failed.", follow.Recording.Id.Wire);
            }
        }
    }

    private async Task LetGoAsync()
    {
        Follow[] stopping = [.. follows.Values];

        follows.Clear();

        foreach (Follow follow in stopping)
        {
            await follow.Stopping.CancelAsync();
        }

        foreach (Follow follow in stopping)
        {
            await EndedAsync(follow);
            follow.Stopping.Dispose();
        }
    }

    private static async Task EndedAsync(Follow follow)
        => await follow.Running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    private static async Task<ProgrammeCopy> CopyAsync(ILearningWorklist worklist, Recording recording, CancellationToken cancellationToken)
        => ProgrammeCopy.Of(recording, await worklist.ProgrammeEndsAtAsync(recording, cancellationToken));

    private string? Source(Recording recording)
        => mounts.OutputRoots.FirstOrDefault(mounted => mounted.Root.Equals(recording.OutputRoot)) is { } mounted
            ? Path.Combine(mounted.Path, recording.FileName.Value)
            : null;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private void Woken(string name)
    {
        if (!WakesOn(name))
        {
            return;
        }

        try
        {
            Volatile.Read(ref waking)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
    }

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        using CancellationTokenSource woken = new();

        Volatile.Write(ref waking, woken);

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(woken.Token, stoppingToken);

            await Task.Delay(waiting, clock, linked.Token);
        }
        catch (OperationCanceledException)
        {
            return !stoppingToken.IsCancellationRequested;
        }
        finally
        {
            Volatile.Write(ref waking, null);
        }

        return true;
    }

    private void Told(LearningLook look)
    {
        if (!look.SaysAnything)
        {
            return;
        }

        logger.LogInformation(
            "A look for recordings to follow for their learning data put back {Recovered} record(s) left running, began "
            + "{Began} follow(s), stopped {Stopped}, made {Waiting} record(s) waiting, and leaves {Following} follow(s) running.",
            look.Recovered,
            look.Began,
            look.Stopped,
            look.Waiting,
            look.Following);
    }

    private sealed record Follow(FollowedRecording Recording, CancellationTokenSource Stopping, Task Running);
}
