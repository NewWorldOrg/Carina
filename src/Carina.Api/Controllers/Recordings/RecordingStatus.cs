using Carina.Api.Services;

namespace Carina.Api.Controllers.Recordings;

public static class RecordingStatus
{
    public static int Of(RecordingFailure failure) => failure switch
    {
        RecordingFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        RecordingFailure.DriverRefused => StatusCodes.Status502BadGateway,
        RecordingFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        RecordingFailure.NowhereToPutPictures => StatusCodes.Status503ServiceUnavailable,
        RecordingFailure.FileOutOfReach => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
