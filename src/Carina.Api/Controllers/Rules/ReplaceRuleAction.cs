using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReplaceRuleAction(RuleService rules) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] SaveRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (RuleIdText.Read(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<RuleResponder>.Error(RuleIdText.Description));
        }

        if (RuleDrafting.Read(request) is not { } drafted)
        {
            return BadRequest(BaseResponder<RuleResponder>.Error(
                RuleInput.Because(RuleDrafting.Fault(request))));
        }

        ServiceResult<Rule, RuleFailure> written = await rules.RewriteAsync(ruleId, drafted, cancellationToken);

        return written.IsSuccess
            ? Ok(BaseResponder<RuleResponder>.Success(RuleResponder.Of(written.Data!)))
            : StatusCode(
                RuleStatus.Of(written.ErrorType),
                BaseResponder<RuleResponder>.Error(written.ErrorMessage!));
    }
}
