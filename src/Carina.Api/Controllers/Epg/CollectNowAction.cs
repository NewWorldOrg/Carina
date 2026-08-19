using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Programmes;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/epg/collect-now")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CollectNowAction(CollectionBoostService boosts) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<BoostStartedResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<BoostStartedResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<BoostRefusedResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        [FromBody] CollectNowRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<BoostOutcome> asked = await boosts.StartAsync(
            request ?? new CollectNowRequest(),
            cancellationToken);
        BoostOutcome outcome = asked.Data!;

        if (outcome.Started is { } started)
        {
            return Accepted(BaseResponder<BoostStartedResponder>.Success(
                BoostStartedResponder.Of(started)));
        }

        if (outcome.NothingMatched)
        {
            return NotFound(BaseResponder<BoostStartedResponder>.Error(
                "Nothing on offer matches what was asked for."));
        }

        return Conflict(new BaseResponder<BoostRefusedResponder>(
            false,
            Because(outcome.Refusal!),
            BoostRefusedResponder.Of(outcome.Refusal!)));
    }

    private static string Because(BoostVerdict verdict)
        => verdict.Refusal switch
        {
            BoostRefusal.OneIsAlreadyRunning => "A boost is already walking; only one runs at a time.",
            BoostRefusal.TooSoonAfterTheLastOne => "The last boost finished too recently to ask for another.",
            _ => "The boost could not be started.",
        };
}
