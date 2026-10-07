using System.Collections.Concurrent;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Integrity;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Segments;
using Carina.Infrastructure.Tests.Integrity;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// The recordings being written, as following them for their learning data reads them, held in memory.
/// </summary>
internal sealed class HeldLearningWorklist : ILearningWorklist
{
    public List<Recording> Recordings { get; } = [];

    public Dictionary<RecordingId, DateTime> Ends { get; } = [];

    public Task<IReadOnlyList<Recording>> BeingRecordedAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>(
            [.. Recordings.Where(recording => recording.IsInFlight && withinReach.Contains(recording.OutputRoot))]);

    public Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) switch
        {
            null => (RecordingStanding?)null,
            { IsInFlight: true } => RecordingStanding.InFlight,
            _ => RecordingStanding.Ended,
        });

    public Task<DateTime?> ProgrammeEndsAtAsync(Recording recording, CancellationToken cancellationToken)
        => Task.FromResult(Ends.TryGetValue(recording.Id, out DateTime ends) ? ends : (DateTime?)null);
}

/// <summary>
/// A follower that follows nothing: it remembers what it was asked to follow, and a follow lasts until
/// it is stopped or told its recording ended, when it settles the record as read to the end.
/// </summary>
internal sealed class HeldFollower(LearningRecords records, DateTime at) : ILearningFollower
{
    public ConcurrentQueue<FollowedRecording> Asked { get; } = new();

    public ConcurrentQueue<RecordingId> Stopped { get; } = new();

    public async Task FollowAsync(FollowedRecording recording, CancellationToken cancellationToken)
    {
        Asked.Enqueue(recording);

        try
        {
            while (!recording.HasEnded && !recording.HasGone)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Stopped.Enqueue(recording.Id);

            throw;
        }

        await records.ChangeAsync(
            recording.Id,
            record =>
            {
                record.Finish(at);

                return true;
            },
            CancellationToken.None);
    }
}

internal sealed class LearningFollowHarness : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    public static readonly OutputRoot Mounted = new("primary");

    public static readonly OutputRoot Unmounted = new("elsewhere");

    private readonly TempTree tree = new();

    private readonly ServiceProvider provider;

    public LearningFollowHarness()
    {
        provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(Records)
            .AddSingleton<ILearningDataRepository>(Data)
            .AddSingleton<ISegmentSettingsRepository>(Settings)
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddSingleton<ILearningWorklist>(Worklist)
            .BuildServiceProvider();

        Store = new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>());
        Follower = new HeldFollower(Store, Now);
    }

    public HeldLearningExtractions Records { get; } = new();

    public HeldLearningData Data { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public HeldLearningWorklist Worklist { get; } = new();

    public LearningRecords Store { get; }

    public HeldFollower Follower { get; }

    public DriverSignalRelay Signals { get; } = new(NullLogger<DriverSignalRelay>.Instance);

    public IntegritySettings Mounts => new() { OutputRoots = [new StorageRootPath(Mounted, tree.Root)] };

    public LearningFollowJob Job(LearningFollowSettings? settings = null)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Follower,
            Store,
            Mounts,
            settings ?? LearningFollowSettings.Default,
            Signals,
            new StillClock(Now),
            NullLogger<LearningFollowJob>.Instance);

    public async Task LearningAsync(bool on) => await Settings.SaveAsync(SegmentSettings.LearningSwitched(on, Now), CancellationToken.None);

    public Recording Recording(OutputRoot? root = null, bool withAFile = true, int eventId = 7301)
    {
        Recording recording = Made(root ?? Mounted, eventId, Now.AddMinutes(-6));

        if (withAFile)
        {
            tree.Holding(recording.FileName.Value, 188 * 4);
        }

        Worklist.Recordings.Add(recording);

        return recording;
    }

    public static Recording Made(OutputRoot root, int eventId, DateTime startedAt, ReservationId? reservation = null)
    {
        RecordingId id = RecordingId.New();

        return Domain.Recordings.Recording.Begin(
            id,
            reservation,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1040), new EventId(eventId), Now.AddMinutes(-5)),
            root,
            RecordingFileName.For(id, ".m2ts"),
            startedAt,
            Now.AddMinutes(54),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [new ProgrammeGenre(7, 0)],
                Now.AddDays(-1),
                AudioMode.Stereo,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            startedAt,
            new TunerDeviceId("synthetic-0"));
    }

    public string Source(Recording recording) => tree.Under(recording.FileName.Value);

    public static void End(Recording recording)
    {
        recording.Abort(Now);
        recording.Settle(RecordingOutcome.Complete, 1_000_000, Now);
    }

    public static LearningExtraction Left(Recording recording, LearningExtractionState state)
        => LearningExtraction.Rehydrate(
            recording.Id,
            state,
            ExtractionVersion.Current,
            TimeSpan.FromSeconds(600),
            [new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30))],
            new ExtractionSound(0, TimeSpan.FromMilliseconds(-33)),
            null,
            0,
            ProgrammeCopy.Of(recording, null),
            Now.AddMinutes(-5),
            Now.AddMinutes(-1));

    public void Dispose()
    {
        provider.Dispose();
        tree.Dispose();
    }

    private sealed class StillClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at, TimeSpan.Zero);
    }
}
