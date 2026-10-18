using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;
using Carina.Domain.Segments;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Segments;

[ApiController]
[Route("api/segments/version")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PatchSegmentVersionAction(SegmentVersionService versions) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<SegmentVersionResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<SegmentVersionResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<SegmentVersionResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<SegmentVersionResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        [FromBody] PatchSegmentVersionRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<SegmentModelVersion, SegmentVersionFailure> chosen = await versions.ChooseAsync(request?.Number, request?.ByHand, cancellationToken);

        if (chosen.Data is { } active)
        {
            return Ok(BaseResponder<SegmentVersionResponder>.Success(SegmentVersionResponder.Of(active)));
        }

        BaseResponder<SegmentVersionResponder> refused = BaseResponder<SegmentVersionResponder>.Error(chosen.ErrorMessage!);

        return chosen.ErrorType switch
        {
            SegmentVersionFailure.LearningOff => Conflict(refused),
            SegmentVersionFailure.NoSuchVersion => NotFound(refused),
            _ => BadRequest(refused),
        };
    }
}
