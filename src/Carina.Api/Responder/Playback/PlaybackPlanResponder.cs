using Carina.Domain.Playback;

namespace Carina.Api.Responder.Playback;

public sealed record PlaybackPlanResponder(
    PlaybackStanding Standing,
    PlaybackRoute Route,
    PlaybackSeeking? Seeking,
    bool CanSeek,
    bool Transcodes,
    bool ShowsAsAWholeRecording,
    string MediaType,
    long? Bytes)
{
    public static PlaybackPlanResponder Of(PlaybackPlan plan, PlaybackFile handover, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(handover);

        return new PlaybackPlanResponder(
            plan.Standing,
            plan.Route,
            plan.Seeking,
            plan.Seeking is PlaybackSeeking.ByRange,
            plan.Transcodes,
            plan.ShowsAsAWholeRecording,
            mediaType,
            plan.Transcodes ? null : handover.Bytes);
    }
}
