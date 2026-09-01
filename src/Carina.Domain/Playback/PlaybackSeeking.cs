namespace Carina.Domain.Playback;

public enum PlaybackSeeking
{
    ByRange = 1,

    ByStartingAgain = 2,
}

public static class PlaybackSeekings
{
    public static PlaybackSeeking? Of(PlaybackRoute route) => route switch
    {
        PlaybackRoute.Direct => PlaybackSeeking.ByRange,
        PlaybackRoute.OnTheFly => PlaybackSeeking.ByStartingAgain,
        PlaybackRoute.Nothing => null,
        _ => throw new ArgumentOutOfRangeException(
            nameof(route),
            route,
            "A recording is played one of the ways named here, or not at all."),
    };
}
