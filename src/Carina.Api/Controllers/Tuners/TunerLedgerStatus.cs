using Carina.Api.Services;

namespace Carina.Api.Controllers.Tuners;

public static class TunerLedgerStatus
{
    public static int Of(TunerLedgerFailure failure) => failure switch
    {
        TunerLedgerFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        TunerLedgerFailure.CapabilityMissing => StatusCodes.Status501NotImplemented,
        TunerLedgerFailure.NoSuchTuner => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status400BadRequest,
    };
}
