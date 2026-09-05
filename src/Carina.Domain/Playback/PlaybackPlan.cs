namespace Carina.Domain.Playback;

public sealed record PlaybackPlan
{
    private PlaybackPlan(
        PlaybackRoute route,
        PlaybackStanding standing,
        PlaybackFile? handover,
        PlaybackRefusal? refusal)
    {
        Route = route;
        Standing = standing;
        Handover = handover;
        Refusal = refusal;
    }

    public PlaybackRoute Route { get; }

    public PlaybackStanding Standing { get; }

    public PlaybackFile? Handover { get; }

    public PlaybackRefusal? Refusal { get; }

    public bool PlaysAtAll => Route is not PlaybackRoute.Nothing;

    public bool Transcodes => Route is PlaybackRoute.OnTheFly;

    public PlaybackSeeking? Seeking => PlaybackSeekings.Of(Route);

    public bool ShowsAsAWholeRecording => Standing is PlaybackStanding.Whole;

    public static PlaybackPlan For(PlaybackSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        PlaybackStanding standing = PlaybackStandings.Of(subject.Outcome);

        if (subject.Outcome is null)
        {
            return Refused(standing, PlaybackRefusal.StillBeingWritten);
        }

        if (subject.BrowserReady.FirstOrDefault(file => file.HoldsAnything) is { } encoded)
        {
            return new PlaybackPlan(PlaybackRoute.Direct, standing, encoded, null);
        }

        if (subject.AsRecorded.Found is not { } recorded)
        {
            return Refused(
                standing,
                subject.AsRecorded.Absence is PlaybackFileAbsence.Gone
                    ? PlaybackRefusal.FileGone
                    : PlaybackRefusal.FileOutOfReach);
        }

        return recorded.HoldsAnything
            ? new PlaybackPlan(PlaybackRoute.OnTheFly, standing, recorded, null)
            : Refused(standing, PlaybackRefusal.NothingWasWritten);
    }

    private static PlaybackPlan Refused(PlaybackStanding standing, PlaybackRefusal refusal)
        => new(PlaybackRoute.Nothing, standing, null, refusal);
}
