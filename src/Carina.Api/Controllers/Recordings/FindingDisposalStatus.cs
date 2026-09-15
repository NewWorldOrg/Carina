using Carina.Api.Services;

namespace Carina.Api.Controllers.Recordings;

public static class FindingDisposalStatus
{
    public static int Of(FindingDisposalFailure failure) => failure switch
    {
        FindingDisposalFailure.NoSuchFinding => StatusCodes.Status404NotFound,
        FindingDisposalFailure.DriverRefused => StatusCodes.Status502BadGateway,
        FindingDisposalFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        FindingDisposalFailure.FilesLeftBehind => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
