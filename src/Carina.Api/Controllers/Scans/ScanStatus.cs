using Carina.Api.Services;

namespace Carina.Api.Controllers.Scans;

public static class ScanStatus
{
    public static int Of(ScanFailure failure) => failure switch
    {
        ScanFailure.NoSuchRun => StatusCodes.Status404NotFound,

        // Told apart from the conflicts, because this is the only refusal a caller cannot wait
        // out: the difference is not coming back, and walking for it again costs minutes.
        ScanFailure.ProposalGone => StatusCodes.Status410Gone,
        _ => StatusCodes.Status409Conflict,
    };
}
