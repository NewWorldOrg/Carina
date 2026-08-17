using Carina.Api.Services;

namespace Carina.Api.Controllers.Scans;

public static class ScanStatus
{
    public static int Of(ScanFailure failure) => failure switch
    {
        ScanFailure.NoSuchRun => StatusCodes.Status404NotFound,

        ScanFailure.ProposalGone => StatusCodes.Status410Gone,
        _ => StatusCodes.Status409Conflict,
    };
}
