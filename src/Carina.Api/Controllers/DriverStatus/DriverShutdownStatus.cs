using Carina.Api.Services;

namespace Carina.Api.Controllers.DriverStatus;

public static class DriverShutdownStatus
{
    public static int Of(DriverShutdownFailure failure) => failure switch
    {
        DriverShutdownFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        DriverShutdownFailure.CapabilityMissing => StatusCodes.Status501NotImplemented,
        DriverShutdownFailure.RecordingInProgress => StatusCodes.Status409Conflict,
        DriverShutdownFailure.DriverInconsistent => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest,
    };
}
