using Carina.Api.Common;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public sealed record PlayedChapters(IReadOnlyList<PlaybackChapter> Chapters, PlaybackFileClock? Clock);

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
