using Carina.Api.Responder.Epg;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Api.Responder.Recordings;

public sealed record RecordingProgrammeResponder(
    int NetworkId,
    int ServiceId,
    int EventId,
    DateTime StartsAt,
    string Name,
    string Summary,
    string Extended,
    IReadOnlyList<ProgrammeGenreResponder> Genres,
    DateTime CapturedAt);

public sealed record RecordingBroadcastGroupResponder(string? Key, BroadcastGroupRole Role);

public sealed record RecordingWindowResponder(DateTime Start, DateTime End, long DurationMs);

public sealed record RecordingDropsResponder(
    bool CcMeasured,
    long? CcDroppedPackets,
    long? CcTotalPackets,
    long? ScrambledPackets,
    long EovfCount,
    DateTime? MeasuredUpdatedAt);

public sealed record RecordingThumbnailResponder(
    ThumbnailState State,
    ThumbnailFault? Fault,
    bool ShowsAnUnfinishedRecording);

public sealed record RecordingFaultResponder(
    RecordingFault Fault,
    TuneFailureKind? TuneFailure,
    string Note,
    DateTime NoticedAt);

public sealed record RecordingInterruptionResponder(
    RecordingFault Fault,
    DateTime OccurredAt,
    DateTime? ResumedAt);

public sealed record RecordingResponder(
    string Id,
    Guid? ReservationId,
    RecordingProgrammeResponder Programme,
    RecordingStanding Standing,
    RecordingOutcome? Outcome,
    IReadOnlyList<RecordingFaultResponder> OutcomeDetail,
    DateTime StartedAt,
    DateTime? StoppedAt,
    DateTime? AbortedAt,
    RecordingWindowResponder ExpectedWindow,
    long WrittenDurationMs,
    int ResumeCount,
    long? FileSizeBytes,
    string OutputRoot,
    string FileName,
    string? TunerDeviceId,
    RecordingDropsResponder Drops,
    RecordingThumbnailResponder Thumbnail,
    RecordingBroadcastGroupResponder BroadcastGroup)
{
    public static RecordingResponder Of(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return new RecordingResponder(
            recording.Id.Wire,
            recording.ReservationId?.Value,
            new RecordingProgrammeResponder(
                recording.NetworkId.Value,
                recording.ServiceId.Value,
                recording.EventId.Value,
                recording.ProgrammeStartsAt,
                recording.SnapshotName,
                recording.SnapshotSummary,
                recording.SnapshotExtended,
                [.. recording.SnapshotGenres.Select(ProgrammeGenreResponder.Of)],
                recording.CapturedAt),
            recording.IsInFlight ? RecordingStanding.InFlight : RecordingStanding.Ended,
            recording.Outcome,
            [.. recording.OutcomeDetail.Select(detail => new RecordingFaultResponder(
                detail.Fault,
                detail.TuneFailure,
                detail.Note,
                detail.NoticedAt))],
            recording.StartedAtActual,
            recording.StoppedAtActual,
            recording.AbortedAt,
            Window(recording),
            recording.WrittenDurationMs,
            recording.ResumeCount,
            recording.FileSizeObserved,
            recording.OutputRoot.Value,
            recording.FileName.Value,
            recording.TunerDeviceId?.Value,
            new RecordingDropsResponder(
                recording.Counters.Measured,
                recording.Counters.Dropped,
                recording.Counters.Total,
                recording.ScrambledPackets,
                recording.EovfCount,
                recording.MeasuredUpdatedAt),
            new RecordingThumbnailResponder(
                recording.ThumbnailState,
                recording.ThumbnailFault,
                recording.ThumbnailShowsAnUnfinishedRecording),
            new RecordingBroadcastGroupResponder(
                recording.BroadcastGroupKey?.Value,
                recording.BroadcastGroupRole));
    }

    internal static RecordingWindowResponder Window(Recording recording)
        => new(
            recording.ExpectedWindowStart,
            recording.ExpectedWindowEnd,
            (long)(recording.ExpectedWindowEnd - recording.ExpectedWindowStart).TotalMilliseconds);
}

public sealed record RecordingListResponder(
    IReadOnlyList<RecordingResponder> Items,
    int Total,
    int CurrentPage,
    int LastPage,
    int PerPage)
{
    public static RecordingListResponder Of(PaginatedList<Recording> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return new RecordingListResponder(
            [.. found.Items.Select(RecordingResponder.Of)],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
