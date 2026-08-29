using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/impact")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ImpactOfRulesAction(RuleService rules) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RuleImpactResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleImpactResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleImpactResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RuleImpactResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        [FromBody] RuleDraftRequest? request,
        CancellationToken cancellationToken)
    {
        if (RuleRehearsing.Read(request) is not (var ruleId, { } drafted))
        {
            return BadRequest(BaseResponder<RuleImpactResponder>.Error(
                RuleInput.Because(RuleRehearsing.Fault(request))));
        }

        ServiceResult<RuleRehearsal, RuleFailure> rehearsed =
            await rules.RehearseAsync(ruleId, drafted, cancellationToken);

        return rehearsed.IsSuccess
            ? Ok(BaseResponder<RuleImpactResponder>.Success(RuleImpactResponder.Of(rehearsed.Data!)))
            : StatusCode(
                RuleStatus.Of(rehearsed.ErrorType),
                BaseResponder<RuleImpactResponder>.Error(rehearsed.ErrorMessage!));
    }
}
