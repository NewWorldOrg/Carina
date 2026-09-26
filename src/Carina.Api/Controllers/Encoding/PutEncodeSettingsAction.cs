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
[Route("api/encoding/settings")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutEncodeSettingsAction(EncodeAutoRunService autoRun) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<EncodeAutoRunResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeAutoRunResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] PutEncodeSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<EncodeAutoRunReading> settled = await autoRun.SettleAsync(
            request?.Automatically,
            request?.MostCores,
            cancellationToken);

        return settled.Data is { } standing
            ? Ok(BaseResponder<EncodeAutoRunResponder>.Success(
                EncodeAutoRunResponder.Of(standing, autoRun.Cores)))
            : BadRequest(BaseResponder<EncodeAutoRunResponder>.Error(settled.ErrorMessage!));
    }
}
