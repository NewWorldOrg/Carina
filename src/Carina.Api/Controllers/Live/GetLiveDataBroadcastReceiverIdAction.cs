using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

using Puppis.Contracts;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/{networkId:int}-{serviceId:int}/data-broadcast/receiver-id")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetLiveDataBroadcastReceiverIdAction(DataBroadcastRelayService relay) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<DataBroadcastReceiverIdResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<DataBroadcastReceiverIdResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<DataBroadcastReceiverIdResponder>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<BaseResponder<DataBroadcastReceiverIdResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<DataBroadcastReceiverIdResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(int networkId, int serviceId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return BadRequest(BaseResponder<DataBroadcastReceiverIdResponder>.Error(LiveDataBroadcastChannel.TheChannelsThereAre));
        }

        ServiceResult<ConditionalAccessCardDto, DataBroadcastRelayRefusal> named = await relay.ReceiverIdAsync(
            channel.Network,
            channel.Service,
            DataBroadcastLineCookie.AllowsTheLine(Request.Cookies),
            DataBroadcastReceiverIdCookie.HandsTheId(Request.Cookies),
            cancellationToken);

        return named.IsSuccess
            ? Ok(BaseResponder<DataBroadcastReceiverIdResponder>.Success(DataBroadcastReceiverIdResponder.Of(named.Data!)))
            : StatusCode(
                LiveDataBroadcastChannel.StatusOf(named.ErrorType),
                BaseResponder<DataBroadcastReceiverIdResponder>.Error(named.ErrorMessage!));
    }
}
