using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Integrity;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/integrity/findings/{findingId}/delete")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class DeleteIntegrityFindingAction(IntegrityService integrity) : ControllerBase
{
    public const string FindingIdDescription =
        "A finding is named by the identifier the most recent check gave it, written as a GUID.";

    [HttpPost]
    [ProducesResponseType<BaseResponder<IntegrityFindingThrownAwayResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<IntegrityFindingThrownAwayResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<IntegrityFindingRefusedResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<IntegrityFindingRefusedResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<IntegrityFindingRefusedResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<IntegrityFindingRefusedResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(string findingId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(findingId, "D", out Guid named) || named == Guid.Empty)
        {
            return BadRequest(BaseResponder<IntegrityFindingThrownAwayResponder>.Error(FindingIdDescription));
        }

        ServiceResult<FindingThrownAway, FindingDisposalFailure> thrown =
            await integrity.ThrowAwayAsync(new IntegrityFindingId(named), cancellationToken);

        return thrown.IsSuccess
            ? Ok(BaseResponder<IntegrityFindingThrownAwayResponder>.Success(
                IntegrityFindingThrownAwayResponder.Of(thrown.Data!)))
            : StatusCode(
                FindingDisposalStatus.Of(thrown.ErrorType),
                new BaseResponder<IntegrityFindingRefusedResponder>(
                    false,
                    thrown.ErrorMessage!,
                    IntegrityFindingRefusedResponder.Of(named, thrown.ErrorType)));
    }
}
