using Carina.Api.Services;

namespace Carina.Api.Controllers.Encoding;

public static class EncodingStatus
{
    public static int Of(EncodingFailure failure) => failure switch
    {
        EncodingFailure.Refused => StatusCodes.Status400BadRequest,
        EncodingFailure.NoSuchProfile => StatusCodes.Status404NotFound,
        EncodingFailure.NoSuchDestination => StatusCodes.Status404NotFound,
        EncodingFailure.NoSuchRecording => StatusCodes.Status404NotFound,
        EncodingFailure.NoSuchJob => StatusCodes.Status404NotFound,
        EncodingFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        EncodingFailure.DriverRefused => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status409Conflict,
    };
}
