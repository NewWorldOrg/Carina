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
}
