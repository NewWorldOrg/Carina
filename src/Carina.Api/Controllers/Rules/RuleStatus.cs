using Carina.Api.Services;

namespace Carina.Api.Controllers.Rules;

public static class RuleStatus
{
    public static int Of(RuleFailure failure) => failure switch
    {
        RuleFailure.NoSuchRule => StatusCodes.Status404NotFound,
        RuleFailure.NotWrittenAsARule => StatusCodes.Status400BadRequest,
        RuleFailure.TunersCannotBeCounted => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
