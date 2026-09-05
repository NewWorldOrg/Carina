using Carina.Api.Services;

namespace Carina.Api.Playback;

public static class PlaybackStatus
{
    public static int Of(PlaybackFailure failure) => failure switch
    {
        PlaybackFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        PlaybackFailure.NothingWasWritten => StatusCodes.Status404NotFound,
        PlaybackFailure.FileGone => StatusCodes.Status404NotFound,
        PlaybackFailure.StillBeingWritten => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
