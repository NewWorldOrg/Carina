using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/{id:guid}/enabled")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class SwitchRuleAction(RuleService rules) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RuleSwitchedResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleSwitchedResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleSwitchedResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] RuleEnabledRequest? request,
        CancellationToken cancellationToken)
    {
        if (RuleIdText.Read(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<RuleSwitchedResponder>.Error(RuleIdText.Description));
        }

        if (request?.Enabled is not { } enabled)
        {
            return BadRequest(BaseResponder<RuleSwitchedResponder>.Error(
                "enabled: a rule is switched on or off, and which of the two is asked for rather than assumed."));
        }

        ServiceResult<RuleSwitched, RuleFailure> switched =
            await rules.SwitchAsync(ruleId, enabled, cancellationToken);

        return switched.IsSuccess
            ? Ok(BaseResponder<RuleSwitchedResponder>.Success(RuleSwitchedResponder.Of(switched.Data!)))
            : StatusCode(
                RuleStatus.Of(switched.ErrorType),
                BaseResponder<RuleSwitchedResponder>.Error(switched.ErrorMessage!));
    }
}
