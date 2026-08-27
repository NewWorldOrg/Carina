using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.TestSupport;

public sealed class HeldRecordings : IRecordingDirectory
{
    public List<Recording> Recordings { get; } = [];

    public Action? WhenHalting { get; set; }

    public Task<PaginatedList<Recording>> ListAsync(RecordingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<Recording> found = Recordings;

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
            found = found.Where(recording => Reads(recording, drops));
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
        IOrderedEnumerable<Recording> ordered = (query.Sort, query.Descending) switch
        {
            (RecordingSort.ProgrammeStartsAt, false) => matched.OrderBy(recording => recording.ProgrammeStartsAt),
            (RecordingSort.ProgrammeStartsAt, true) => matched.OrderByDescending(recording => recording.ProgrammeStartsAt),
            (_, true) => matched.OrderByDescending(recording => recording.StartedAtActual),
            _ => matched.OrderBy(recording => recording.StartedAtActual),
        };

        return Task.FromResult(new PaginatedList<Recording>(
            [
                .. ordered
                    .ThenBy(recording => recording.Id.Value, ByTheOrderTheDatabaseReadsThem.Comparer)
                    .Skip((query.Page - 1) * query.PerPage)
                    .Take(query.PerPage)
                    .Select(Apart),
            ],
            matched.Length,
            query.Page,
            query.PerPage));
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
                held.CapturedAt),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault);

    private static bool Reads(Recording recording, DropReading drops)
        => drops switch
        {
            DropReading.Dropped => recording.Counters.Measured && recording.Counters.Dropped > 0,
            DropReading.Clean => recording.Counters.Measured && recording.Counters.Dropped == 0,
            _ => !recording.Counters.Measured,
        };
}
