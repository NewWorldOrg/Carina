using Carina.Api.Services;

namespace Carina.Api.Controllers.Services;

public static class CatalogStatus
{
    public static int Of(CatalogFailure failure) => failure switch
    {
        CatalogFailure.NoSuchService => StatusCodes.Status404NotFound,
        CatalogFailure.NoSuchCandidate => StatusCodes.Status404NotFound,
        CatalogFailure.NoTunerReceivesIt => StatusCodes.Status422UnprocessableEntity,
        CatalogFailure.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
