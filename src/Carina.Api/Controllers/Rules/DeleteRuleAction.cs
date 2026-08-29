using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Infrastructure.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/{id:guid}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class DeleteRuleAction(RuleService rules) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<RuleRetirementResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleRetirementResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleRetirementResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (RuleIdText.Read(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<RuleRetirementResponder>.Error(RuleIdText.Description));
        }

        ServiceResult<RuleRetirement, RuleFailure> retired = await rules.RetireAsync(ruleId, cancellationToken);

        return retired.IsSuccess
            ? Ok(BaseResponder<RuleRetirementResponder>.Success(
                RuleRetirementResponder.Of(retired.Data!)))
            : StatusCode(
                RuleStatus.Of(retired.ErrorType),
                BaseResponder<RuleRetirementResponder>.Error(retired.ErrorMessage!));
    }
}
