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
/// The chapters a playback offer plays, on the clock of the file it hands over: the stretches the recording
/// stands with, by the answer a person gave while it stands and otherwise by the version of the model in use,
/// placed by <see cref="PlaybackFileClock"/> and offered to be skipped by the threshold the settings stand at, a
/// stretch an answer gave whatever the threshold. While learning is off and the recording stands with nothing,
/// an artefact is played with the chapters its encode marked, each taken as certain, only when the reader of the
/// source marked them; chapters an encode took from the segments, an answer since withdrawn or a detection by
/// another version, are not played. An artefact whose timeline was not kept has no stretch placed on it. Whichever
/// chapters are played, the clock that places the recording's time on the file is handed back beside them. Nothing
/// here writes.
/// </summary>
public sealed class PlaybackChapterService(
    ISegmentSettingsRepository settings,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections,
    ISegmentAnswerRepository answers,
    IEncodeJobRepository jobs,
    IEncodeChapterRepository marked)
{
    public async Task<ServiceResult<PlayedChapters>> ChaptersAsync(
        RecordingId id,
        PlaybackOffer offer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(offer);

        EncodeJobId? artefact = offer.Plan.Route is PlaybackRoute.Direct ? offer.Artefact : null;
        EncodeJob? made = artefact is { } encoded ? await jobs.FindAsync(encoded, cancellationToken) : null;
        PlaybackFileClock? clock = artefact is null ? PlaybackFileClock.AsItIs : ClockOf(made);
        SegmentSettingsStanding standing = SegmentSettingsStanding.Over(await settings.ReadAsync(cancellationToken));
        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        SegmentStanding segments = SegmentStanding.Of(
            await answers.FindAsync(id, cancellationToken),
            await detections.FindAsync(id, version.Number, cancellationToken));

        if (segments.Source is SegmentSource.None && !standing.Learning && artefact is not null)
        {
            return Played(PlaybackChapter.Of(await MarkedAsync(made, cancellationToken), PlaybackFileClock.AsItIs, standing.SkipThreshold), clock);
        }

        return Played(clock is null ? [] : PlaybackChapter.Of(segments.Played, clock, standing.SkipThreshold), clock);
    }

    private static ServiceResult<PlayedChapters> Played(IReadOnlyList<PlaybackChapter> chapters, PlaybackFileClock? clock)
        => ServiceResult<PlayedChapters>.Success(new PlayedChapters(chapters, clock));

    private static PlaybackFileClock? ClockOf(EncodeJob? made)
        => made is { Timeline: { } timeline } ? PlaybackFileClock.Of(timeline) : null;

    private async Task<IReadOnlyList<SegmentSpan>> MarkedAsync(EncodeJob? made, CancellationToken cancellationToken)
    {
        if (made is not { Chapters: null or { Detector: not ChapterDetectorName.Segments, AnsweredAt: null } })
        {
            return [];
        }

        return [.. (await marked.ListForJobAsync(made.Id, cancellationToken)).Select(chapter => new SegmentSpan(chapter.StartsAt, chapter.EndsAt, KindOf(chapter.Kind), 1))];
    }

    private static SegmentKind KindOf(ChapterKind kind) => kind is ChapterKind.Break ? SegmentKind.Break : SegmentKind.Programme;
}
