using Carina.Api.Common;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// The chapters a plan plays and the clock of the file it hands over, or no clock when the file's time cannot be
/// told from the recording's.
/// </summary>
public sealed record PlayedChapters(IReadOnlyList<PlaybackChapter> Chapters, PlaybackFileClock? Clock);

/// <summary>
/// The chapters a playback offer plays, on the clock of the file it hands over: the stretches the version of the
/// model in use detected, placed by <see cref="PlaybackFileClock"/> and offered to be skipped by the threshold the
/// settings stand at, and none while that version has not detected the recording. An artefact whose timeline was
/// not kept has no stretch placed on it. The clock that places the recording's time on the file is handed back
/// beside the chapters. Nothing here writes.
/// </summary>
public sealed class PlaybackChapterService(
    ISegmentSettingsRepository settings,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections,
    IEncodeJobRepository jobs)
{
    public async Task<ServiceResult<PlayedChapters>> ChaptersAsync(
        RecordingId id,
        PlaybackOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        EncodeJobId? artefact = offer.Plan.Route is PlaybackRoute.Direct ? offer.Artefact : null;
        PlaybackFileClock? clock = artefact is { } encoded ? ClockOf(await jobs.FindAsync(encoded, cancellationToken)) : PlaybackFileClock.AsItIs;
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(await settings.ReadAsync(cancellationToken));
        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        SegmentStanding segments = SegmentStanding.Of(await detections.FindAsync(id, version.Number, cancellationToken));
        IReadOnlyList<PlaybackChapter> chapters = clock is null ? [] : PlaybackChapter.Of(segments.Played, clock, standing.SkipThreshold);

        return ServiceResult<PlayedChapters>.Success(new PlayedChapters(chapters, clock));
    }

    private static PlaybackFileClock? ClockOf(EncodeJob? made)
        => made is { Timeline: { } timeline } ? PlaybackFileClock.Of(timeline) : null;
}
