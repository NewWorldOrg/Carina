using Carina.Api.OpenApi;
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

    public static readonly QueryInput WhereThePlayingStarts = QueryInput.Seconds(
        PlayDelivery.Position,
        "The second of the recording playing starts at, counted from where the recording begins. "
        + "Seconds may be fractional, and asking for none starts at the beginning. It moves the picture only "
        + "where the recording is transcoded as it plays; one handed over as it is, is seeked by a byte range.");

    public static readonly QueryInput WhichProfileThePictureIsEncodedIn = QueryInput.OneOfThese(
        PlayDelivery.Quality,
        "The profile the picture is encoded in while it is transcoded as it plays.",
        [.. LiveProfile.All.Select(profile => profile.Name)],
        PlayDelivery.Ordinarily.Name);
}
