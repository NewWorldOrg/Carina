using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.TestSupport;

public sealed class HeldRecordings : IRecordingDirectory
{
    public List<Recording> Recordings { get; } = [];

    public Action? WhenHalting { get; set; }

    public Action? WhenDiscarding { get; set; }

    public Task<RecordingListing> ListAsync(
        RecordingQuery query,
        QualityBands bands,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(bands);

        IEnumerable<Recording> found = Recordings;

        if (query.Keyword.Words.Count > 0)
        {
            found = found.Where(recording => query.Keyword.Words.All(word =>
                Folded(recording).Contains(word, StringComparison.Ordinal)));
        }

        if (query.Standing is { } standing)
        {
            found = found.Where(recording => recording.IsInFlight == (standing is RecordingStanding.InFlight));
        }

        if (query.Outcomes.Count > 0)
        {
            found = found.Where(recording =>
                recording.Outcome is { } outcome && query.Outcomes.Contains(outcome));
        }

        if (query.Drops is { } drops)
        {
            Func<Recording, bool> clean = RecordingQuality.CountedClean(bands).Compile();
            found = found.Where(recording => Reads(recording, drops, clean));
        }

        if (query.Channels.Count > 0)
        {
            found = found.Where(recording => query.Channels.Any(channel =>
                channel.NetworkId == recording.NetworkId.Value
                && channel.ServiceId == recording.ServiceId.Value));
        }

        if (query.From is { } from)
        {
            found = found.Where(recording => recording.StartedAtActual >= from);
        }

        if (query.To is { } to)
        {
            found = found.Where(recording => recording.StartedAtActual < to);
        }

        Recording[] matched = [.. found];
        IEnumerable<Recording> onwards = query.After is { } after
            ? matched.Where(recording => Compared(recording, after) > 0)
            : matched;
        Recording[] remaining = [.. onwards];
        int ahead = query.After is null ? (query.Page - 1) * query.PerPage : matched.Length - remaining.Length;
        IEnumerable<Recording> ordered = remaining.Order(Comparer<Recording>.Create((left, right) => InOrder(left, right, query)));

        return Task.FromResult(RecordingListing.Of(
            [.. (query.After is null ? ordered.Skip(ahead) : ordered).Take(query.PerPage + 1).Select(Apart)],
            matched.Length,
            ahead,
            query));
    }

    private static int InOrder(Recording left, Recording right, RecordingQuery query)
    {
        int compared = RecordingCursor.KeyOf(left, query.Sort).CompareTo(RecordingCursor.KeyOf(right, query.Sort));

        if (compared is 0)
        {
            compared = ByTheOrderTheDatabaseReadsThem.Comparer.Compare(left.Id.Value, right.Id.Value);
        }

        return query.Descending ? -compared : compared;
    }

    private static int Compared(Recording recording, RecordingCursor after)
    {
        int compared = RecordingCursor.KeyOf(recording, after.Sort).CompareTo(after.Key);

        if (compared is 0)
        {
            compared = ByTheOrderTheDatabaseReadsThem.Comparer.Compare(recording.Id.Value, after.Id.Value);
        }

        return after.Descending ? -compared : compared;
    }

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(
            Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) is { } held ? Apart(held) : null);

    public Task<RecordingHalt> HaltAsync(
        RecordingId id,
        RecordingStopReason reason,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reason);

        WhenHalting?.Invoke();

        if (Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) is not { } held)
        {
            return Task.FromResult(RecordingHalt.NoSuchRecording);
        }

        if (!held.IsInFlight)
        {
            return Task.FromResult(RecordingHalt.AlreadyEnded);
        }

        held.Note(new OutcomeDetail(RecordingFault.StoppedByHand, null, reason.Value, at));
        held.Abort(at);

        return Task.FromResult(RecordingHalt.Written);
    }

    public Task<RecordingErasureNote> NoteErasureAsync(
        RecordingId id,
        RecordingErasure erasure,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(erasure);

        if (Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) is not { } held)
        {
            return Task.FromResult(RecordingErasureNote.NoSuchRecording);
        }

        if (held.IsInFlight)
        {
            return Task.FromResult(RecordingErasureNote.StillRecording);
        }

        held.Erased(erasure, at);

        return Task.FromResult(RecordingErasureNote.Noted);
    }

    public Task<RecordingDiscard> DiscardAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        WhenDiscarding?.Invoke();

        if (Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) is not { } held)
        {
            return Task.FromResult(RecordingDiscard.NoSuchRecording);
        }

        if (held.IsInFlight)
        {
            return Task.FromResult(RecordingDiscard.StillRecording);
        }

        Recordings.Remove(held);

        return Task.FromResult(RecordingDiscard.Discarded);
    }

    private static Recording Apart(Recording held)
        => Recording.Rehydrate(
            held.Id,
            held.ReservationId,
            held.Programme,
            held.OutputRoot,
            held.FileName,
            held.FileSizeObserved,
            held.ObservedAt,
            held.StartedAtActual,
            held.StoppedAtActual,
            held.AbortedAt,
            held.WrittenDurationMs,
            held.ResumeCount,
            held.Interruptions,
            held.ExpectedWindowStart,
            held.ExpectedWindowEnd,
            held.PromisedWindowEnd,
            held.Outcome,
            held.OutcomeDetail,
            held.Counters,
            held.Positions,
            held.ScrambledPackets,
            held.EovfCount,
            held.MeasuredUpdatedAt,
            held.TunerDeviceId,
            held.ThumbnailState,
            new ProgrammeSnapshot(
                held.SnapshotName,
                held.SnapshotSummary,
                held.SnapshotExtended,
                held.SnapshotGenres,
                held.CapturedAt,
                held.SnapshotAudio,
                held.SnapshotSounds),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault,
            held.LeftBehindAt,
            held.FilesLeftBehind,
            held.EncodeWhenRecorded,
            held.DescrambledAt,
            held.Gaps,
            held.Carried,
            held.CountedSessionOpenedAt,
            held.CaptionState,
            held.CaptionsMadeAt,
            held.CaptionPictures,
            held.CaptionAttempts,
            held.DataBroadcast,
            held.DataBroadcastMadeAt);

    private static string Folded(Recording recording)
        => ProgrammeSearchText.Folded(
            recording.SnapshotName
            + ProgrammeSearchText.BetweenNameAndSummary
            + recording.SnapshotSummary
            + ProgrammeSearchText.BetweenNameAndSummary
            + recording.SnapshotExtended);

    private static bool Reads(Recording recording, DropReading drops, Func<Recording, bool> clean)
        => drops switch
        {
            DropReading.Dropped => recording.Counters.Measured && recording.Counters.Dropped > 0,
            DropReading.Clean => clean(recording),
            _ => !recording.Counters.Measured,
        };
}
