using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Domain.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.DataBroadcast;

[ApiController]
[Route("api/data-broadcast/settings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetDataBroadcastSettingsAction(DataBroadcastSettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<DataBroadcastSettingsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<DataBroadcastSettingsStanding> read = await settings.ReadAsync(cancellationToken);

        return Ok(BaseResponder<DataBroadcastSettingsResponder>.Success(DataBroadcastSettingsResponder.Of(read.Data!)));
    }
}
