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
[Route("api/services/{networkId:int}-{serviceId:int}/candidate-channels")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class AddCandidateChannelAction(ChannelCatalogService channelCatalogService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<BaseResponder<BroadcastServiceResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromBody] AddCandidateChannelRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Tuning is not { } asked)
        {
            return BadRequest(BaseResponder<BroadcastServiceResponder>.Error(
                "tuning: a candidate channel names the system and channel it tunes."));
        }

        if (asked.ToParameters(out string? problem) is not { } tuning)
        {
            return BadRequest(BaseResponder<BroadcastServiceResponder>.Error($"tuning: {problem}"));
        }

        ServiceResult<ServiceWithChannels, CatalogFailure> result = await channelCatalogService.AddCandidateAsync(
            new NetworkId(networkId),
            new ServiceId(serviceId),
            tuning,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                CatalogStatus.Of(result.ErrorType),
                BaseResponder<BroadcastServiceResponder>.Error(result.ErrorMessage!));
        }

        return StatusCode(
            StatusCodes.Status201Created,
            BaseResponder<BroadcastServiceResponder>.Success(
                BroadcastServiceResponder.Of(result.Data!)));
    }
}
