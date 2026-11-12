using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Driver;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Puppis.Contracts;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// What one look at the recordings being written did: how many records it put back after a start, how many
/// follows it began, and how many follows were running when it was done.
/// </summary>
public sealed record LearningLook(int Recovered, int Began, int Following)
{
    public bool SaysAnything => Recovered > 0 || Began > 0;
}

/// <summary>
/// Follows every recording being written under an output root this process can read, one follow
/// each, for its learning data. It looks after <see cref="LearningFollowSettings.BeforeFirstLook"/>,
/// then every <see cref="LearningFollowSettings.BetweenLooks"/> and whenever the driver tells of a
/// recording's progress. The first look puts back the records a follow left running when the process
/// last stopped: a recording still being written is followed again from its head, and one that has
/// ended or gone waits to be read. A recording being written whose file is there is followed from its
/// head, its record made following, one left waiting included, and a follow is told when its recording
/// ends or goes. A follow that comes to its end, rather than being stopped, is told to the matching against
/// the catalog (<see cref="FollowedToTheEnd"/>). Stopping the process stops every follow and leaves its
/// record for the next start.
/// </summary>
public sealed class LearningFollowJob(
    IServiceScopeFactory scopes,
    ILearningFollower follower,
    FollowedToTheEnd finished,
    LearningRecords records,
    IntegritySettings mounts,
    LearningFollowSettings settings,
    IDriverSignals signals,
    TimeProvider clock,
    ILogger<LearningFollowJob> logger) : BackgroundService
{
    public const int MostRecoveredAtOnce = 1000;

    private readonly Dictionary<RecordingId, Follow> follows = [];

    private readonly SignalledWait wait = new(clock);

    private bool recovered;

    public IReadOnlyCollection<RecordingId> Following => [.. follows.Keys];

    public static bool WakesOn(string name) => string.Equals(name, DriverEvents.RecordingProgress, StringComparison.Ordinal);

    public async Task<LearningLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ILearningWorklist worklist = scope.ServiceProvider.GetRequiredService<ILearningWorklist>();
        int putBack = recovered
            ? 0
            : await RecoverAsync(scope.ServiceProvider.GetRequiredService<LearningExtractionRecoveryDomainService>(), cancellationToken);

        recovered = true;

        IReadOnlyList<Recording> recording = await worklist.BeingRecordedAsync(
            [.. mounts.OutputRoots.Select(mounted => mounted.Root)],
            cancellationToken);

        Reap();
        await TellAsync(worklist, recording, cancellationToken);

        int began = await FollowEachAsync(scope.ServiceProvider.GetRequiredService<ProgrammeEndDomainService>(), recording, cancellationToken);

        return new LearningLook(putBack, began, follows.Count);
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

    private async Task<int> RecoverAsync(LearningExtractionRecoveryDomainService recovery, CancellationToken cancellationToken)
    {
        int putBack = 0;

        foreach (LearningExtraction left in await records.ListAsync(LearningExtractionState.Following, MostRecoveredAtOnce, cancellationToken))
        {
            if (!await recovery.PutsBackAsync(left, cancellationToken))
            {
                continue;
            }

            bool putBackHere = await ChangedAsync(
                left.RecordingId,
                record => record.PutBack(LearningExtractionState.Following, ExtractionVersion.Current, Now()),
                cancellationToken);

            putBack += putBackHere ? 1 : 0;
        }

        return putBack;
    }

    private async Task<int> FollowEachAsync(
        ProgrammeEndDomainService programmeEnds,
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

            if (await FollowedFromTheHeadAsync(programmeEnds, being, cancellationToken))
            {
                Begin(being, source);
                began++;
            }
        }

        return began;
    }

    private async Task<bool> FollowedFromTheHeadAsync(
        ProgrammeEndDomainService programmeEnds,
        Recording recording,
        CancellationToken cancellationToken)
    {
        DateTime now = Now();

        if (await records.FindAsync(recording.Id, cancellationToken) is not null)
        {
            return await ChangedAsync(recording.Id, found => found.FollowFromTheHead(ExtractionVersion.Current, now), cancellationToken);
        }

        await records.AddAsync(
            LearningExtraction.Following(recording.Id, await CopyAsync(programmeEnds, recording, cancellationToken), ExtractionVersion.Current, now),
            cancellationToken);

        return true;
    }

    private async Task<bool> ChangedAsync(RecordingId id, Func<LearningExtraction, bool> change, CancellationToken cancellationToken)
        => await records.ChangeAsync(id, change, cancellationToken) is ExtractionChange.Written;

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
        Task running = Task.Run(() => FollowToTheEndAsync(followed, stopping.Token), CancellationToken.None);

        follows[recording.Id] = new Follow(followed, stopping, running);
    }

    private async Task FollowToTheEndAsync(FollowedRecording followed, CancellationToken cancellationToken)
    {
        await follower.FollowAsync(followed, cancellationToken);

        if (!cancellationToken.IsCancellationRequested)
        {
            finished.Tell(followed.Id);
        }
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

    private static async Task<ProgrammeCopy> CopyAsync(
        ProgrammeEndDomainService programmeEnds,
        Recording recording,
        CancellationToken cancellationToken)
        => ProgrammeCopy.Of(recording, await programmeEnds.EndsAtAsync(recording, cancellationToken));

    private string? Source(Recording recording)
        => mounts.OutputRoots.FirstOrDefault(mounted => mounted.Root.Equals(recording.OutputRoot)) is { } mounted
            ? Path.Combine(mounted.Path, recording.FileName.Value)
            : null;

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private void Woken(string name)
    {
        if (WakesOn(name))
        {
            wait.Signal();
        }
    }

    private Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken) => wait.WaitAsync(waiting, stoppingToken);

    private void Told(LearningLook look)
    {
        if (!look.SaysAnything)
        {
            return;
        }

        logger.LogInformation(
            "A look for recordings to follow for their learning data put back {Recovered} record(s) left running, began "
            + "{Began} follow(s), and leaves {Following} follow(s) running.",
            look.Recovered,
            look.Began,
            look.Following);
    }

    private sealed record Follow(FollowedRecording Recording, CancellationTokenSource Stopping, Task Running);
}
