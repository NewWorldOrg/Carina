using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateRuleAction(RuleService rules) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<RuleResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] SaveRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (RuleDrafting.Read(request) is not { } drafted)
        {
            return BadRequest(BaseResponder<RuleResponder>.Error(
                RuleInput.Because(RuleDrafting.Fault(request))));
        }

        ServiceResult<Rule, RuleFailure> written = await rules.WriteAsync(drafted, cancellationToken);

        return Created(
            new Uri($"/api/rules/{written.Data!.Id.Value}", UriKind.Relative),
            BaseResponder<RuleResponder>.Success(RuleResponder.Of(written.Data!)));
    }
}

public static class RuleDrafting
{
    public static RuleInputFault Fault(SaveRuleRequest? request)
    {
        if (request is null)
        {
            return RuleInputFault.NameIsMissing;
        }

        RuleInputFault named = RuleInput.NameFault(request.Name);

        return named is RuleInputFault.None
            ? RuleInput.DraftFault(
                request.Query,
                request.Priority,
                request.MarginBeforeSeconds,
                request.MarginAfterSeconds)
            : named;
    }

    public static RuleDraft? Read(SaveRuleRequest? request)
        => request is not null && Fault(request) is RuleInputFault.None
            ? new RuleDraft(
                request.Name!.Trim(),
                new RuleQuery(request.Query!),
                ReservationInput.PriorityOf(request.Priority) ?? Priority.Default,
                request.Enabled ?? true,
                ReservationInput.MarginOf(request.MarginBeforeSeconds) ?? Margin.None,
                ReservationInput.MarginOf(request.MarginAfterSeconds) ?? Margin.None)
            : null;
}
