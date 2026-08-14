using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services/{networkId:int}-{serviceId:int}/selected-channel")]
public sealed class PutSelectedChannelAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpPut]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromBody] SelectedChannelRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await channelCatalogService.SelectAsync(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            request?.CandidateChannelId is { } chosen ? new CandidateChannelId(chosen) : null,
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
