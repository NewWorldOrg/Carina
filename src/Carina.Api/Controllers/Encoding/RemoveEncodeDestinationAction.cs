using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/destinations/{id:guid}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class RemoveEncodeDestinationAction(EncodeDestinationService destinations) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<EncodeRemovalResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeRemovalResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeRemovalResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<EncodeRemovalResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (EncodingIdText.Destination(id) is not { } destinationId)
        {
            return BadRequest(BaseResponder<EncodeRemovalResponder>.Error(EncodingIdText.Description));
        }

        ServiceResult<EncodeDefinitionRemoved, EncodingFailure> removed = await destinations.RemoveAsync(destinationId, cancellationToken);

        return removed.IsSuccess
            ? Ok(BaseResponder<EncodeRemovalResponder>.Success(EncodeRemovalResponder.Of(removed.Data!)))
            : StatusCode(EncodingStatus.Of(removed.ErrorType), BaseResponder<EncodeRemovalResponder>.Error(removed.ErrorMessage!));
    }
}
