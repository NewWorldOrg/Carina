namespace Carina.Domain.Playback;

public sealed record PlaybackPlan
{
    private PlaybackPlan(
        PlaybackRoute route,
        PlaybackStanding standing,
        PlaybackFile? handover,
        PlaybackRefusal? refusal,
        PlaybackFallback? fellBack)
    {
        Route = route;
        Standing = standing;
        Handover = handover;
        Refusal = refusal;
        FellBack = fellBack;
    }

    public PlaybackRoute Route { get; }

    public PlaybackStanding Standing { get; }

    public PlaybackFile? Handover { get; }

    public PlaybackRefusal? Refusal { get; }

    public PlaybackFallback? FellBack { get; }

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
            return Refused(standing, PlaybackRefusal.StillBeingWritten, null);
        }

        PlaybackFallback? fellBack = null;

        foreach (PlaybackFileSearch encoded in subject.BrowserReady)
        {
            if (encoded.Found is not { } artefact)
            {
                fellBack ??= encoded.Absence is PlaybackFileAbsence.Gone
                    ? PlaybackFallback.EncodedFileGone
                    : PlaybackFallback.EncodedFileOutOfReach;

                continue;
            }

            if (artefact.HoldsAnything)
            {
                return new PlaybackPlan(PlaybackRoute.Direct, standing, artefact, null, null);
            }

            fellBack ??= PlaybackFallback.EncodedFileHoldsNothing;
        }

        if (subject.AsRecorded.Found is not { } recorded)
        {
            return Refused(
                standing,
                subject.AsRecorded.Absence is PlaybackFileAbsence.Gone
                    ? PlaybackRefusal.FileGone
                    : PlaybackRefusal.FileOutOfReach,
                fellBack);
        }

        return recorded.HoldsAnything
            ? new PlaybackPlan(PlaybackRoute.OnTheFly, standing, recorded, null, fellBack)
            : Refused(standing, PlaybackRefusal.NothingWasWritten, fellBack);
    }

    private static PlaybackPlan Refused(
        PlaybackStanding standing,
        PlaybackRefusal refusal,
        PlaybackFallback? fellBack)
        => new(PlaybackRoute.Nothing, standing, null, refusal, fellBack);
}
