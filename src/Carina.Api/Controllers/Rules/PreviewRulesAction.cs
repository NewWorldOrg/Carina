using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules/preview")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class PreviewRulesAction(RuleService rules) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<RulePreviewResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<RulePreviewResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<RulePreviewResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<RulePreviewResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        [FromBody] RuleDraftRequest? request,
        CancellationToken cancellationToken)
    {
        if (RuleRehearsing.Read(request) is not (var ruleId, { } drafted))
        {
            return BadRequest(BaseResponder<RulePreviewResponder>.Error(
                RuleInput.Because(RuleRehearsing.Fault(request))));
        }

        ServiceResult<RuleRehearsal, RuleFailure> rehearsed =
            await rules.RehearseAsync(ruleId, drafted, cancellationToken);

        return rehearsed.IsSuccess
            ? Ok(BaseResponder<RulePreviewResponder>.Success(RulePreviewResponder.Of(rehearsed.Data!)))
            : StatusCode(
                RuleStatus.Of(rehearsed.ErrorType),
                BaseResponder<RulePreviewResponder>.Error(rehearsed.ErrorMessage!));
    }
}

public static class RuleRehearsing
{
    public static RuleInputFault Fault(RuleDraftRequest? request)
        => request is null
            ? RuleInputFault.QueryIsMissing
            : RuleInput.DraftFault(
                request.Query,
                request.Priority,
                request.MarginBeforeSeconds,
                request.MarginAfterSeconds);

    public static (RuleId? Id, RuleDraft? Draft) Read(RuleDraftRequest? request)
        => request is not null && Fault(request) is RuleInputFault.None
            ? (
                request.RuleId is { } named ? RuleIdText.Read(named) : null,
                new RuleDraft(
                    "a draft",
                    new RuleQuery(request.Query!),
                    ReservationInput.PriorityOf(request.Priority) ?? Priority.Default,
                    true,
                    ReservationInput.MarginOf(request.MarginBeforeSeconds) ?? Margin.None,
                    ReservationInput.MarginOf(request.MarginAfterSeconds) ?? Margin.None))
            : (null, null);
}
