using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services/{networkId:int}-{serviceId:int}")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetServiceAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
    {
        if (ServiceKeyText.Read(networkId, serviceId) is not { } key)
        {
            return BadRequest(BaseResponder<BroadcastServiceResponder>.Error(ServiceKeyText.Description));
        }

        ServiceResult<ServiceWithChannels, CatalogFailure> result = await channelCatalogService.FindAsync(
            key.Network,
            key.Service,
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
