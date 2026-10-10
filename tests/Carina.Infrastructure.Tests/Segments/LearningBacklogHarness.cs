using System.Collections.Concurrent;

using Carina.Domain.Captions;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Segments;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// The recordings that have ended and their records, read the way the store reads them, held in memory.
/// </summary>
internal sealed class HeldLearningBacklog(
    HeldLearningWorklist worklist,
    HeldLearningExtractions records,
    HeldLearningData data) : ILearningBacklog
{
    public Task<IReadOnlyList<BackloggedRecording>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int skip,
        int atMost,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BackloggedRecording>>(
        [
            .. worklist.Recordings
                .Where(recording => recording.Outcome is not (null or RecordingOutcome.Failed) && withinReach.Contains(recording.OutputRoot))
                .Select(recording => new BackloggedRecording(recording, records.Row(recording.Id)))
                .Where(next => next.Record?.AwaitsReading(ExtractionVersion.Current, next.Recording.StoppedAtActual) ?? true)
                .OrderByDescending(next => next.Recording.StartedAtActual)
                .Skip(skip)
                .Take(atMost),
        ]);

    public Task<IReadOnlyList<LearningExtraction>> UncaptionedAsync(int skip, int atMost, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<LearningExtraction>>(
        [
            .. records.All()
                .Where(record => record.State is LearningExtractionState.Done or LearningExtractionState.Partial && record.ReadThrough > TimeSpan.Zero)
                .Select(record => (Record: record, Recording: worklist.Recordings.FirstOrDefault(recording => recording.Id.Equals(record.RecordingId))))
                .Where(pair => pair.Recording is { CaptionState: CaptionState.Ready, CaptionsMadeAt: { } made } && !Captioned(pair.Record, made))
                .OrderByDescending(pair => pair.Recording!.StartedAtActual)
                .Skip(skip)
                .Take(atMost)
                .Select(pair => pair.Record),
        ]);

    private bool Captioned(LearningExtraction record, DateTime made)
        => data.Of(record.RecordingId).Any(block => block is { Kind: LearningDataKind.CaptionPresence, Chunk: 0 }
                                                    && block.WrittenAt >= made
                                                    && block.WrittenAt >= record.UpdatedAt);
}

/// <summary>
/// A reader that reads nothing: it remembers what it was asked to read and how far it got, and a reading
/// lasts until it is released, stopped or told its recording went, when it settles the record as read to
/// the end or as far as it got.
/// </summary>
internal sealed class HeldReader(LearningRecords records, DateTime at) : ILearningFollower
{
    public static readonly TimeSpan ReadsAtOnce = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<RecordingId, TaskCompletionSource> released = new();

    public ConcurrentQueue<FollowedRecording> Asked { get; } = new();

    public ConcurrentQueue<RecordingId> Stopped { get; } = new();

    public bool LeavesItsRecord { get; set; }

    public void Release(RecordingId id) => Released(id).TrySetResult();

    public async Task FollowAsync(FollowedRecording recording, CancellationToken cancellationToken)
    {
        Asked.Enqueue(recording);

        await ChangeAsync(recording, record => record.Reached(ReadsAtOnce, at));

        try
        {
            while (!Released(recording.Id).Task.IsCompleted && !recording.HasGone)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Stopped.Enqueue(recording.Id);

            throw;
        }

        if (LeavesItsRecord)
        {
            return;
        }

        await ChangeAsync(recording, record => Settle(record, recording.HasGone));
    }

    private void Settle(LearningExtraction record, bool gone)
    {
        if (gone)
        {
            record.FinishPartway(at);

            return;
        }

        record.Finish(at);
    }

    private async Task ChangeAsync(FollowedRecording recording, Action<LearningExtraction> change)
        => await records.ChangeAsync(
            recording.Id,
            record =>
            {
                change(record);

                return true;
            },
            CancellationToken.None);

    private TaskCompletionSource Released(RecordingId id)
        => released.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

/// <summary>
/// An importer that reads nothing: it remembers the copies it was asked to import, and an import lasts until
/// it is released or stopped, when it settles the record as read to the end, or as failed when asked to.
/// </summary>
internal sealed class HeldImporter(LearningRecords records, DateTime at) : IReducedCopyImporter
{
    public static readonly TimeSpan ImportsAtOnce = TimeSpan.FromMinutes(25);

    private readonly ConcurrentDictionary<RecordingId, TaskCompletionSource> released = new();

    public ConcurrentQueue<ReducedCopy> Asked { get; } = new();

    public ConcurrentQueue<RecordingId> Stopped { get; } = new();

    public bool ReleasesAtOnce { get; set; }

    public bool Fails { get; set; }

    public void Release(RecordingId id) => Released(id).TrySetResult();

    public async Task ImportAsync(ReducedCopy copy, CancellationToken cancellationToken)
    {
        Asked.Enqueue(copy);

        try
        {
            while (!ReleasesAtOnce && !Released(copy.Id).Task.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            Stopped.Enqueue(copy.Id);

            throw;
        }

        await records.ChangeAsync(copy.Id, Settle, CancellationToken.None);
    }

    private bool Settle(LearningExtraction record)
    {
        if (Fails)
        {
            record.Fail(ExtractionFailure.StreamMissing, "no picture", at);

            return true;
        }

        record.Reached(ImportsAtOnce, at);
        record.Finish(at);

        return true;
    }

    private TaskCompletionSource Released(RecordingId id)
        => released.GetOrAdd(id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal sealed class HeldCaptionRecords : ICaptionRecords
{
    public Dictionary<RecordingId, CaptionRecord> Kept { get; } = [];

    public Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Kept.GetValueOrDefault(id));

    public Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Kept.TryGetValue(id, out CaptionRecord? record) ? record.StartsAt : (TimeSpan?)null);
}

internal sealed class HeldWatching : IWatching
{
    public bool Anyone { get; set; }
}

/// <summary>
/// The recordings the worklist holds, read as the ledger.
/// </summary>
internal sealed class WorklistRecordings(HeldLearningWorklist worklist) : IRecordingRepository
{
    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(worklist.Recordings.FirstOrDefault(recording => recording.Id.Equals(id)));

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. worklist.Recordings.Where(recording => recording.IsInFlight)]);

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(ReservationId reservationId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. worklist.Recordings.Where(recording => reservationId.Equals(recording.ReservationId))]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        worklist.Recordings.Add(recording);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class LearningBacklogHarness : IDisposable
{
    private readonly TempTree tree = new();

    private readonly TempTree copies = new();

    private readonly ServiceProvider provider;

    public LearningBacklogHarness()
    {
        Backlog = new HeldLearningBacklog(Worklist, Records, Data);
        provider = new ServiceCollection()
            .AddSingleton<ILearningExtractionRepository>(Records)
            .AddSingleton<ILearningDataRepository>(Data)
            .AddSingleton<ISegmentSettingsRepository>(Settings)
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddSingleton<ILearningWorklist>(Worklist)
            .AddSingleton<ILearningBacklog>(Backlog)
            .AddSingleton<IRecordingRepository>(new WorklistRecordings(Worklist))
            .AddSingleton<IReservationRepository>(Reservations)
            .AddSingleton<IWatching>(Watching)
            .AddScoped<IBusynessReader, BusynessReader>()
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .BuildServiceProvider();

        Store = new LearningRecords(provider.GetRequiredService<IServiceScopeFactory>());
        Reader = new HeldReader(Store, Now);
        Importer = new HeldImporter(Store, Now);
    }

    public HeldLearningExtractions Records { get; } = new();

    public HeldLearningData Data { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public HeldLearningWorklist Worklist { get; } = new();

    public HeldLearningBacklog Backlog { get; }

    public HeldReservations Reservations { get; } = new();

    public HeldWatching Watching { get; } = new();

    public HeldCaptionRecords Captions { get; } = new();

    public LearningRecords Store { get; }

    public HeldReader Reader { get; }

    public HeldImporter Importer { get; }

    public TempTree Copies => copies;

    public IntegritySettings Mounts => new() { OutputRoots = [new StorageRootPath(Mounted, tree.Root)] };

    public LearningBacklogJob Job(LearningBacklogSettings? settings = null, LearningImportSettings? import = null)
        => new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Reader,
            new ReducedCopyImports(
                import ?? new LearningImportSettings { ImportFrom = copies.Root },
                Importer,
                Store,
                new StillClock(Now),
                NullLogger<ReducedCopyImports>.Instance),
            Store,
            Captions,
            Mounts,
            settings ?? LearningBacklogSettings.Default,
            new StillClock(Now),
            NullLogger<LearningBacklogJob>.Instance);

    public async Task LearningAsync(bool on) => await Settings.SaveAsync(SegmentSettings.LearningSwitched(on, Now), CancellationToken.None);

    /// <summary>
    /// A recording that ended <paramref name="startedMinutesAgo"/> minutes after it started, with its file
    /// unless asked otherwise.
    /// </summary>
    public Recording Ended(int startedMinutesAgo, int eventId, bool withAFile = true, OutputRoot? root = null, bool failed = false)
    {
        Recording recording = Made(root ?? Mounted, eventId, Now.AddMinutes(-startedMinutesAgo));
        DateTime stopped = Now.AddMinutes(-startedMinutesAgo + 1);

        if (failed)
        {
            recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, stopped));
            recording.Settle(RecordingOutcome.Failed, 0, stopped);
        }
        else
        {
            recording.Abort(stopped);
            recording.Settle(RecordingOutcome.Complete, 1_000_000, stopped);
        }

        if (withAFile)
        {
            tree.Holding(recording.FileName.Value, 188 * 4);
        }

        Worklist.Recordings.Add(recording);

        return recording;
    }

    public Recording BeingRecorded(int eventId)
    {
        Recording recording = Made(Mounted, eventId, Now.AddMinutes(-1));

        Worklist.Recordings.Add(recording);

        return recording;
    }

    public string Source(Recording recording) => tree.Under(recording.FileName.Value);

    /// <summary>
    /// A reduced copy on the shelf the import reads, of the recording given or of one with no row.
    /// </summary>
    public CopyDescription Copied(string name, Recording? of = null, params string[] leftOut)
    {
        CopyDescription description = of is null ? new CopyDescription() : new CopyDescription { Id = of.Id };

        ReducedCopies.Write(copies.Root, name, description, leftOut);

        return description;
    }

    public void Dispose()
    {
        provider.Dispose();
        tree.Dispose();
        copies.Dispose();
    }

    private sealed class StillClock(DateTime at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(at, TimeSpan.Zero);
    }
}
