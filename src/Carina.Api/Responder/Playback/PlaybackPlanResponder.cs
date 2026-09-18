using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Playback;

public sealed record PlaybackChapterResponder(double StartsAtSec, double EndsAtSec, ChapterKind Kind)
{
    public static PlaybackChapterResponder Of(EncodeChapter chapter)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        return new PlaybackChapterResponder(
            chapter.StartsAt.TotalSeconds,
            chapter.EndsAt.TotalSeconds,
            chapter.Kind);
    }
}

public sealed record PlaybackPlanResponder(
    PlaybackStanding Standing,
    PlaybackRoute Route,
    PlaybackSource Source,
    PlaybackSource? Alternative,
    PlaybackSeeking? Seeking,
    bool CanSeek,
    bool Transcodes,
    bool ShowsAsAWholeRecording,
    string MediaType,
    long? Bytes,
    double? ResumeAtSec,
    IReadOnlyList<SoundTrack> Sounds,
    IReadOnlyList<PlaybackChapterResponder> Chapters)
{
    public static PlaybackPlanResponder Of(
        PlaybackPlan plan,
        PlaybackFile handover,
        string mediaType,
        TimeSpan? resumeAt,
        IReadOnlyList<SoundTrack> sounds,
        IReadOnlyList<PlaybackChapterResponder> chapters)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(handover);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(chapters);

        return new PlaybackPlanResponder(
            plan.Standing,
            plan.Route,
            plan.Source!.Value,
            plan.Alternative,
            plan.Seeking,
            plan.Seeking is PlaybackSeeking.ByRange,
            plan.Transcodes,
            plan.ShowsAsAWholeRecording,
            mediaType,
            plan.Transcodes ? null : handover.Bytes,
            resumeAt?.TotalSeconds,
            sounds,
            chapters);
    }
}
