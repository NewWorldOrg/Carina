using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services/{networkId:int}-{serviceId:int}")]
public sealed class GetServiceAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var result = await channelCatalogService.FindAsync(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                CatalogStatus.Of(result.ErrorType),
                BaseResponder<BroadcastServiceResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<BroadcastServiceResponder>.Success(
            BroadcastServiceResponder.Of(result.Data!)));
    }
}
