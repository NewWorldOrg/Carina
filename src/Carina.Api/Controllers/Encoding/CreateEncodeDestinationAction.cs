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
[Route("api/encoding/destinations")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateEncodeDestinationAction(EncodeDestinationService destinations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<BaseResponder<EncodeDestinationResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke([FromBody] CreateEncodeDestinationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(BaseResponder<EncodeDestinationResponder>.Error(
                "A destination is defined by label, outputRoot and defaultProfileId."));
        }

        ServiceResult<EncodeDestination, EncodingFailure> defined = await destinations.DefineAsync(
            new EncodeDestinationDraft(
                request.Label,
                request.OutputRoot,
                EncodingIdText.Profile(request.DefaultProfileId)),
            cancellationToken);

        return defined.IsSuccess
            ? Created(
                new Uri("/api/encoding/destinations", UriKind.Relative),
                BaseResponder<EncodeDestinationResponder>.Success(EncodeDestinationResponder.Of(defined.Data!)))
            : StatusCode(EncodingStatus.Of(defined.ErrorType), BaseResponder<EncodeDestinationResponder>.Error(defined.ErrorMessage!));
    }
}
