using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Quality;
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
    AudioMode Audio,
    int Sounds,
    IReadOnlyList<ProgrammeGenreResponder> Genres,
    DateTime CapturedAt);

public sealed record RecordingBroadcastGroupResponder(string? Key, BroadcastGroupRole Role);

public sealed record RecordingWindowResponder(DateTime Start, DateTime End, long DurationMs);

public sealed record RecordingDropsResponder(
    QualityLevel Quality,
    QualityLevel ScrambleQuality,
    bool CcMeasured,
    long? CcDroppedPackets,
    long? CcTotalPackets,
    long? ScrambledPackets,
    long? EovfCount,
    DateTime? MeasuredUpdatedAt);

public sealed record RecordingEncodeResponder(EncodeStanding Standing);

public sealed record RecordingThumbnailResponder(
    ThumbnailState State,
    ThumbnailFault? Fault,
    bool ShowsAnUnfinishedRecording);

public sealed record RecordingFaultResponder(
    RecordingFault Fault,
    TuneFailureKind? TuneFailure,
    string Note,
    DateTime NoticedAt);

public sealed record RecordingUnfinishedDeletionResponder(DateTime LeftBehindAt, int? FilesLeft)
{
    public static RecordingUnfinishedDeletionResponder? Of(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return recording.LeftBehindAt is { } at
            ? new RecordingUnfinishedDeletionResponder(at, recording.FilesLeftBehind)
            : null;
    }
}

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
    DateTime PromisedWindowEnd,
    long WrittenDurationMs,
    int ResumeCount,
    long? FileSizeBytes,
    DateTime? ObservedAt,
    string OutputRoot,
    string FileName,
    string? TunerDeviceId,
    RecordingDropsResponder Drops,
    RecordingThumbnailResponder Thumbnail,
    RecordingBroadcastGroupResponder BroadcastGroup,
    RecordingEncodeResponder Encode,
    RecordingUnfinishedDeletionResponder? UnfinishedDeletion)
{
    public static RecordingResponder Of(RecordingSeen seen)
    {
        ArgumentNullException.ThrowIfNull(seen);

        Recording recording = seen.Recording;
        RecordingQuality quality = RecordingQuality.Of(recording.Counters, recording.ScrambledPackets, seen.Quality);

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
                recording.SnapshotAudio,
                recording.SnapshotSounds,
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
            recording.PromisedWindowEnd,
            recording.WrittenDurationMs,
            recording.ResumeCount,
            recording.FileSizeObserved,
            recording.ObservedAt,
            recording.OutputRoot.Value,
            recording.FileName.Value,
            recording.TunerDeviceId?.Value,
            new RecordingDropsResponder(
                quality.Overall,
                quality.Scrambled,
                recording.Counters.Measured,
                recording.Counters.Dropped,
                recording.Counters.Total,
                recording.ScrambledPackets,
                recording.Counters.Measured ? recording.EovfCount : null,
                recording.MeasuredUpdatedAt),
            new RecordingThumbnailResponder(
                recording.ThumbnailState,
                recording.ThumbnailFault,
                recording.ThumbnailShowsAnUnfinishedRecording),
            new RecordingBroadcastGroupResponder(
                recording.BroadcastGroupKey?.Value,
                recording.BroadcastGroupRole),
            new RecordingEncodeResponder(seen.Encode),
            RecordingUnfinishedDeletionResponder.Of(recording));
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
    public static RecordingListResponder Of(RecordingPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        PaginatedList<Recording> found = page.Found;

        return new RecordingListResponder(
            [
                .. found.Items.Select(recording => RecordingResponder.Of(
                    new RecordingSeen(recording, page.Encoding.For(recording.Id), page.Quality))),
            ],
            found.Total,
            found.CurrentPage,
            found.LastPage,
            found.PerPage);
    }
}
