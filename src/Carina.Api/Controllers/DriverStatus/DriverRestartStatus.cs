using Carina.Api.Services;

namespace Carina.Api.Controllers.DriverStatus;

public static class DriverRestartStatus
{
    public static int Of(DriverRestartFailure failure) => failure switch
    {
        DriverRestartFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        DriverRestartFailure.CapabilityMissing => StatusCodes.Status501NotImplemented,
        DriverRestartFailure.RecordingInProgress => StatusCodes.Status409Conflict,
        DriverRestartFailure.DriverInconsistent => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status400BadRequest,
    };
}
