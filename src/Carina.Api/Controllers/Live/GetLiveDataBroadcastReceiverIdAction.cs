using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Contracts;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

/// <summary>
/// Names the receiver's identifier a live channel's data broadcast document may be given through <c>getIRDID</c>:
/// the CA system and the ID of the card the driver last heard from. Refused 403 unless the browser asking allows
/// communication and hands the identifier over, 404 where the channel cannot be watched live here or no card has
/// answered, and 503 where the driver could not be asked. The answer is never stored on the way, and the ID is never
/// written to a log. Opened with the reader's own session only, never with a ticket.
/// </summary>
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
