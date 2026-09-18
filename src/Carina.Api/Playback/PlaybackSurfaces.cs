using Carina.Api.OpenApi;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public static class PlaybackSurfaces
{
    public const string Tag = "videos";

    public const string PlayingIsCalled = "playVideo";

    public const string ThePictureIsCalled = "getVideoThumbnail";

    public const string TheFrameIsCalled = "getVideoScrubFrame";

    public const string HowARecordingIsPlayedInABrowser =
        "Plays a recording. Asked with Accept: application/json it answers the plan alone - how the recording "
        + "ended, whether it is transcoded as it plays, and whether seeking is a byte range or a restart. "
        + "Asked for anything else it answers the picture itself.";

    public const string ThePictureDrawnOfARecording = "The picture drawn of a recording once it had ended.";

    public const string AFrameFromWhereTheSliderIs = "One frame taken out of a recording at the second asked for.";

    public static readonly QueryInput WhereTheFrameIsTakenFrom = QueryInput.Seconds(
        ScrubDelivery.Position,
        "The second of the recording the frame is taken from, counted from where the recording begins. "
        + "Seconds may be fractional, and asking for none takes the first frame.");

    public static readonly QueryInput WhereThePlayingStarts = QueryInput.SecondsWithNoFixedDefault(
        PlayDelivery.Position,
        "The second of the recording playing starts at, counted from where the recording begins. "
        + "Seconds may be fractional. Asking for none starts where this reader last left this recording, and "
        + "at the beginning where they have not watched it before; asking for zero starts at the beginning "
        + "whatever was left. It moves the picture only where the recording is transcoded as it plays; one "
        + "handed over as it is, is seeked by a byte range, and the plan says where the watching got to so "
        + "that a player can seek there itself.");

    public static readonly QueryInput WhichProfileThePictureIsEncodedIn = QueryInput.OneOfThese(
        PlayDelivery.Quality,
        "The profile the picture is encoded in while it is transcoded as it plays. Asking for none opens at "
        + "what this machine encodes at, which depends on the encoder it has and so has no fixed default here; "
        + "GET /api/live/profiles names it, marked as the unasked one, and the answer is the same for a "
        + "recording as it is for a live channel.",
        [.. LiveProfile.All.Select(profile => profile.Name)],
        null);

    public static readonly QueryInput WhichSoundIsCarried = QueryInput.OneOfThese(
        PlayDelivery.Sound,
        "The sound carried with the picture while the recording is transcoded as it plays. Asking for none "
        + "carries the main sound, as it always did. The plan names the sounds this recording can be asked "
        + "for; one handed over as it is names none, because it carries the one sound it was encoded with.",
        SoundTracks.Names,
        SoundTracks.MainIsCalled);

    public static readonly QueryInput WhichOfTheTwoFilesIsPlayed = QueryInput.OneOfThese(
        PlayDelivery.Source,
        "Which of the two files a recording can be played from is played. Asking for the artefact hands over "
        + "the one encoded of this recording where there is one a browser plays, and transcodes the recording "
        + "itself while playing where there is not; asking for none does the same, as it always did. Asking "
        + "for the recording transcodes the recording itself while playing even where an artefact was made of "
        + "it, and is refused where the recording is no longer on the disk rather than quietly handing over "
        + "the artefact. Either way the transcoder is shared with live channels, so a recording asked for as "
        + "it was recorded takes one of the few pictures this machine transcodes at once. The plan names the "
        + "one it plays and the other one it could be asked for.",
        PlaybackSources.Names,
        PlaybackSources.ArtefactIsCalled);
}
