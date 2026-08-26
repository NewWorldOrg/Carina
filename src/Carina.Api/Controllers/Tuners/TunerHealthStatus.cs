using Carina.Api.Services;

namespace Carina.Api.Controllers.Tuners;

public static class TunerHealthStatus
{
    public static int Of(TunerHealthFailure failure) => failure switch
    {
        TunerHealthFailure.CapacityUnknown => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest,
    };
}
