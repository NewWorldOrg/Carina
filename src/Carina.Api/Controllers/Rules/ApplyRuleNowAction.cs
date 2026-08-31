using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Infrastructure.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/{id:guid}/apply-now")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ApplyRuleNowAction(RuleService rules) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RuleApplicationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RuleApplicationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RuleApplicationResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RuleApplicationRefusedResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<RuleApplicationResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (RuleIdText.Read(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<RuleApplicationResponder>.Error(RuleIdText.Description));
        }

        ServiceResult<RuleApplyOutcome, RuleFailure> asked =
            await rules.ApplyNowAsync(ruleId, cancellationToken);

        if (!asked.IsSuccess)
        {
            return StatusCode(
                RuleStatus.Of(asked.ErrorType),
                BaseResponder<RuleApplicationResponder>.Error(asked.ErrorMessage!));
        }

        RuleApplyOutcome outcome = asked.Data!;

        return outcome.Run is { Pass.Applied: { } applied } ran
            ? Ok(BaseResponder<RuleApplicationResponder>.Success(RuleApplicationResponder.Of(ran, applied)))
            : Conflict(new BaseResponder<RuleApplicationRefusedResponder>(
                false,
                RuleService.Because(outcome.Refusal!),
                RuleApplicationRefusedResponder.Of(outcome.Refusal!)));
    }
}
