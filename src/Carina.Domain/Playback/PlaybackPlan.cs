using Carina.Domain.Streaming;

namespace Carina.Domain.Playback;

public sealed record PlaybackPlan
{
    private PlaybackPlan(
        PlaybackRoute route,
        PlaybackStanding standing,
        PlaybackFile? handover,
        PlaybackRefusal? refusal,
        PlaybackFallback? fellBack,
        PlaybackSource? source,
        PlaybackSource? alternative)
    {
        Route = route;
        Standing = standing;
        Handover = handover;
        Refusal = refusal;
        FellBack = fellBack;
        Source = source;
        Alternative = alternative;
    }

    public PlaybackRoute Route { get; }

    public PlaybackStanding Standing { get; }

    public PlaybackFile? Handover { get; }

    public PlaybackRefusal? Refusal { get; }

    public PlaybackFallback? FellBack { get; }

    public PlaybackSource? Source { get; }

    public PlaybackSource? Alternative { get; }

    public bool PlaysAtAll => Route is not PlaybackRoute.Nothing;

    public bool Transcodes => Route is PlaybackRoute.OnTheFly;

    public PlaybackSeeking? Seeking => PlaybackSeekings.Of(Route);

    public bool ShowsAsAWholeRecording => Standing is PlaybackStanding.Whole;

    public static PlaybackPlan For(PlaybackSubject subject)
        => For(subject, SoundTrack.Main, SoundArrangement.TheMainSoundAlone);

    public static PlaybackPlan For(PlaybackSubject subject, SoundTrack wanted, SoundArrangement carried)
        => For(subject, wanted, carried, PlaybackSource.Artefact);

    public static PlaybackPlan For(
        PlaybackSubject subject,
        SoundTrack wanted,
        SoundArrangement carried,
        PlaybackSource from)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(carried);

        if (!Enum.IsDefined(from))
        {
            throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "A recording is played from the artefact made of it or from the recording itself.");
        }

        PlaybackStanding standing = PlaybackStandings.Of(subject.Outcome);

        if (subject.Outcome is null)
        {
            return Refused(standing, PlaybackRefusal.StillBeingWritten, null, null);
        }

        ArtefactAtHand encoded = TheArtefactAmongThem(subject.BrowserReady);
        bool theArtefactWouldBeAsked = from is PlaybackSource.Artefact && TheArtefactCarriesIt(wanted, carried);

        if (theArtefactWouldBeAsked && encoded.File is { } artefact)
        {
            return new PlaybackPlan(
                PlaybackRoute.Direct,
                standing,
                artefact,
                null,
                null,
                PlaybackSource.Artefact,
                subject.AsRecorded.Found is { HoldsAnything: true } ? PlaybackSource.Recording : null);
        }

        PlaybackFallback? fellBack = theArtefactWouldBeAsked ? encoded.Trouble : null;
        PlaybackSource? alternative = encoded.File is null ? null : PlaybackSource.Artefact;

        if (subject.AsRecorded.Found is not { } recorded)
        {
            return Refused(
                standing,
                subject.AsRecorded.Absence is PlaybackFileAbsence.Gone
                    ? PlaybackRefusal.FileGone
                    : PlaybackRefusal.FileOutOfReach,
                fellBack,
                alternative);
        }

        return recorded.HoldsAnything
            ? new PlaybackPlan(
                PlaybackRoute.OnTheFly,
                standing,
                recorded,
                null,
                fellBack,
                PlaybackSource.Recording,
                alternative)
            : Refused(standing, PlaybackRefusal.NothingWasWritten, fellBack, alternative);
    }

    private static ArtefactAtHand TheArtefactAmongThem(IReadOnlyList<PlaybackFileSearch> browserReady)
    {
        PlaybackFallback? trouble = null;

        foreach (PlaybackFileSearch encoded in browserReady)
        {
            if (encoded.Found is not { } artefact)
            {
                trouble ??= encoded.Absence is PlaybackFileAbsence.Gone
                    ? PlaybackFallback.EncodedFileGone
                    : PlaybackFallback.EncodedFileOutOfReach;

                continue;
            }

            if (artefact.HoldsAnything)
            {
                return new ArtefactAtHand(artefact, null);
            }

            trouble ??= PlaybackFallback.EncodedFileHoldsNothing;
        }

        return new ArtefactAtHand(null, trouble);
    }

    private static bool TheArtefactCarriesIt(SoundTrack wanted, SoundArrangement carried)
        => wanted is SoundTrack.Main || !carried.Holds(wanted);

    private static PlaybackPlan Refused(
        PlaybackStanding standing,
        PlaybackRefusal refusal,
        PlaybackFallback? fellBack,
        PlaybackSource? alternative)
        => new(PlaybackRoute.Nothing, standing, null, refusal, fellBack, null, alternative);

    private readonly record struct ArtefactAtHand(PlaybackFile? File, PlaybackFallback? Trouble);
}
