using Carina.Api.Common;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// The chapters a playback offer plays, on the clock of the file it hands over: the stretches the recording
/// stands with, by the answer a person gave while it stands and otherwise by the version of the model in use,
/// placed by <see cref="PlaybackFileClock"/> and offered to be skipped by the threshold the settings stand at, a
/// stretch an answer gave whatever the threshold. While learning is off and the recording stands with nothing,
/// an artefact is played with the chapters its encode marked, each taken as certain. An artefact whose timeline
/// was not kept has no stretch placed on it. Nothing here writes.
/// </summary>
public sealed class PlaybackChapterService(
    ISegmentSettingsRepository settings,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections,
    ISegmentAnswerRepository answers,
    IEncodeJobRepository jobs,
    IEncodeChapterRepository marked)
{
    public async Task<ServiceResult<IReadOnlyList<PlaybackChapter>>> ChaptersAsync(
        RecordingId id,
        PlaybackOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        EncodeJobId? artefact = offer.Plan.Route is PlaybackRoute.Direct ? offer.Artefact : null;
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(await settings.ReadAsync(cancellationToken));
        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        SegmentStanding segments = SegmentStanding.Of(
            await answers.FindAsync(id, cancellationToken),
            await detections.FindAsync(id, version.Number, cancellationToken));

        if (segments.Source is SegmentSource.None && !standing.Learning && artefact is { } encoded)
        {
            return Chapters(PlaybackChapter.Of(await MarkedAsync(encoded, cancellationToken), PlaybackFileClock.AsItIs, standing.SkipThreshold));
        }

        PlaybackFileClock? clock = artefact is { } made ? await ClockAsync(made, cancellationToken) : PlaybackFileClock.AsItIs;

        return Chapters(clock is null ? [] : PlaybackChapter.Of(segments.Played, clock, standing.SkipThreshold));
    }

    private static ServiceResult<IReadOnlyList<PlaybackChapter>> Chapters(IReadOnlyList<PlaybackChapter> chapters)
        => ServiceResult<IReadOnlyList<PlaybackChapter>>.Success(chapters);

    private async Task<PlaybackFileClock?> ClockAsync(EncodeJobId artefact, CancellationToken cancellationToken)
        => await jobs.FindAsync(artefact, cancellationToken) is { Timeline: { } timeline } ? PlaybackFileClock.Of(timeline) : null;

    private async Task<IReadOnlyList<SegmentSpan>> MarkedAsync(EncodeJobId artefact, CancellationToken cancellationToken)
        => [.. (await marked.ListForJobAsync(artefact, cancellationToken)).Select(chapter => new SegmentSpan(chapter.StartsAt, chapter.EndsAt, KindOf(chapter.Kind), 1))];

    private static SegmentKind KindOf(ChapterKind kind) => kind is ChapterKind.Break ? SegmentKind.Break : SegmentKind.Programme;
}
