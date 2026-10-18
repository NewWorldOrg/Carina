using System.ComponentModel;

using Carina.Api.Responder.Encoding;
using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

using PlaybackChapter = Carina.Domain.Segments.PlaybackChapter;
using PlaybackFileClock = Carina.Domain.Segments.PlaybackFileClock;
using SegmentKind = Carina.Domain.Segments.SegmentKind;

namespace Carina.Api.Responder.Playback;

/// <summary>
/// A chapter of the file a plan hands over, on that file's clock to the hundredth of a second, what it is, and
/// whether a button offers to skip it. A chapter of a kind the plan has no word for is left out.
/// </summary>
public sealed record PlaybackChapterResponder(double StartsAtSec, double EndsAtSec, ChapterKind Kind, bool Skippable)
{
    public static IReadOnlyList<PlaybackChapterResponder> Of(IReadOnlyList<PlaybackChapter> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);

        return [.. chapters.Select(Of).OfType<PlaybackChapterResponder>()];
    }

    private static PlaybackChapterResponder? Of(PlaybackChapter chapter)
        => KindOf(chapter.Stretch.Kind) is { } kind
            ? new PlaybackChapterResponder(Seconds(chapter.Stretch.Starts), Seconds(chapter.Stretch.Ends), kind, chapter.Skippable)
            : null;

    private static ChapterKind? KindOf(SegmentKind kind) => kind switch
    {
        SegmentKind.Programme => ChapterKind.Programme,
        SegmentKind.Break => ChapterKind.Break,
        _ => null,
    };

    private static double Seconds(TimeSpan at) => Math.Round(at.TotalSeconds, 2, MidpointRounding.AwayFromZero);
}

public sealed record PlaybackArtefactFileResponder(EncodeCodec Codec);

public sealed record PlaybackPlanResponder(
    PlaybackStanding Standing,
    PlaybackRoute Route,
    PlaybackSource Source,
    PlaybackSource? Alternative,
    IReadOnlyList<PlaybackSource> ExternalPlayerSources,
    PlaybackSeeking? Seeking,
    bool CanSeek,
    bool Transcodes,
    bool ShowsAsAWholeRecording,
    string MediaType,
    long? Bytes,
    double? ResumeAtSec,
    IReadOnlyList<SoundTrack> Sounds,
    IReadOnlyList<PlaybackChapterResponder> Chapters,
    CaptionStanding Captions,
    PlaybackArtefactFileResponder? ArtefactFile,
    [property: Description(PlaybackPlanResponder.HowFarTheRecordingsTimeStandsAhead)] double? RecordingOffsetSec)
{
    public const string HowFarTheRecordingsTimeStandsAhead =
        "How many seconds a time in the recording, as /api/recordings/{id}/segments gives it, stands ahead of the "
        + "same moment in the file this plan plays: the file's time is the recording's time less this. 0 for the "
        + "recording itself, and for an artefact the head the encode that made it skipped. A time that falls before 0 "
        + "or past the end of the file is not in the file. Null for an artefact whose timeline was not kept, on which "
        + "no time in the recording can be placed.";

    public static PlaybackPlanResponder Of(
        PlaybackPlan plan,
        PlaybackFile handover,
        IReadOnlyList<PlaybackSource> externalPlayerSources,
        string mediaType,
        TimeSpan? resumeAt,
        IReadOnlyList<SoundTrack> sounds,
        IReadOnlyList<PlaybackChapterResponder> chapters,
        CaptionStanding captions,
        EncodeCodec? artefactCodec,
        PlaybackFileClock? clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(handover);
        ArgumentNullException.ThrowIfNull(externalPlayerSources);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(chapters);

        return new PlaybackPlanResponder(
            plan.Standing,
            plan.Route,
            plan.Source!.Value,
            plan.Alternative,
            externalPlayerSources,
            plan.Seeking,
            plan.Seeking is PlaybackSeeking.ByRange,
            plan.Transcodes,
            plan.ShowsAsAWholeRecording,
            mediaType,
            plan.Transcodes ? null : handover.Bytes,
            resumeAt?.TotalSeconds,
            sounds,
            chapters,
            captions,
            artefactCodec is { } codec ? new PlaybackArtefactFileResponder(codec) : null,
            clock is { } placing ? Math.Round(placing.HeadSkip.TotalSeconds, EncodeTimelineResponder.Places) : null);
    }
}
