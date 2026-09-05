using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/destinations")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListEncodeDestinationsAction(EncodeDestinationService destinations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeDestinationListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<EncodeDestination>> listed = await destinations.ListAsync(cancellationToken);

        return Ok(BaseResponder<EncodeDestinationListResponder>.Success(EncodeDestinationListResponder.Of(listed.Data!)));
    }
}
