using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/settings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetEncodeSettingsAction(EncodeAutoRunService autoRun) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeAutoRunResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<EncodeAutoRunReading> read = await autoRun.ReadAsync(cancellationToken);

        return Ok(BaseResponder<EncodeAutoRunResponder>.Success(
            EncodeAutoRunResponder.Of(read.Data!, autoRun.Cores)));
    }
}
