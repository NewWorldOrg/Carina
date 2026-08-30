using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/{id:guid}")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetRuleAction(RuleService rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (RuleIdText.Read(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<RuleResponder>.Error(RuleIdText.Description));
        }

        ServiceResult<Rule, RuleFailure> found = await rules.FindAsync(ruleId, cancellationToken);

        return found.IsSuccess
            ? Ok(BaseResponder<RuleResponder>.Success(RuleResponder.Of(found.Data!)))
            : StatusCode(
                RuleStatus.Of(found.ErrorType),
                BaseResponder<RuleResponder>.Error(found.ErrorMessage!));
    }
}
