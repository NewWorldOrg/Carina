using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/destinations/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseEncodeDestinationAction(EncodeDestinationService destinations) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] ReviseEncodeDestinationRequest? request,
        CancellationToken cancellationToken)
    {
        if (EncodingIdText.Destination(id) is not { } destinationId)
        {
            return BadRequest(BaseResponder<EncodeDestinationResponder>.Error(EncodingIdText.Description));
        }

        if (request is null)
        {
            return BadRequest(BaseResponder<EncodeDestinationResponder>.Error(
                "A destination is defined by label, outputRoot and defaultProfileId, and a change carries every one of them rather than the ones that moved."));
        }

        ServiceResult<EncodeDestination, EncodingFailure> revised = await destinations.ReviseAsync(
            destinationId,
            new EncodeDestinationDraft(request.Label, request.OutputRoot, EncodingIdText.Profile(request.DefaultProfileId)),
            cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<EncodeDestinationResponder>.Success(EncodeDestinationResponder.Of(revised.Data!)))
            : StatusCode(EncodingStatus.Of(revised.ErrorType), BaseResponder<EncodeDestinationResponder>.Error(revised.ErrorMessage!));
    }
}
