using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Services;
using Carina.Api.Services;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Services;

[ApiController]
[Route("api/services/{networkId:int}-{serviceId:int}/candidate-channels/{candidateChannelId:guid}")]
public sealed class DeleteCandidateChannelAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        Guid candidateChannelId,
        CancellationToken cancellationToken)
    {
        ServiceResult<ServiceWithChannels, CatalogFailure> result = await channelCatalogService.RemoveCandidateAsync(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            new CandidateChannelId(candidateChannelId),
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
