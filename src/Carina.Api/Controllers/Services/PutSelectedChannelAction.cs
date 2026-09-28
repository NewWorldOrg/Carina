using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services/{networkId:int}-{serviceId:int}/selected-channel")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutSelectedChannelAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpPut]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromBody] SelectedChannelRequest? request,
        CancellationToken cancellationToken)
    {
        if (ServiceKeyText.Read(networkId, serviceId) is not { } key)
        {
            return BadRequest(BaseResponder<BroadcastServiceResponder>.Error(ServiceKeyText.Description));
        }

        CandidateChannelId? candidate = request?.CandidateChannelId is { } chosen
            ? CandidateChannelIdText.Read(chosen)
            : null;

        if (request?.CandidateChannelId is not null && candidate is null)
        {
            return BadRequest(BaseResponder<BroadcastServiceResponder>.Error(CandidateChannelIdText.Description));
        }

        ServiceResult<ServiceWithChannels, CatalogFailure> result = await channelCatalogService.SelectAsync(
            key.Network,
            key.Service,
            candidate,
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
