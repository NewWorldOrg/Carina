using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

/// <summary>
/// Sends a text from a live channel's data broadcast document to a broadcaster's server and hands back what came
/// back, the way <c>transmitTextDataOverIP</c> returns it. Anything the relay refuses or the server fails at is a
/// result in a success, so that the document reads it; the request itself is refused only where it names no
/// address or character set, where the browser asking has not allowed communication, or where the channel cannot
/// be watched live here. Opened with the reader's own session only, never with a ticket.
/// </summary>
[ApiController]
[Route("api/live/{networkId:int}-{serviceId:int}/data-broadcast/transmit")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class TransmitLiveDataBroadcastAction(DataBroadcastRelayService relay) : ControllerBase
{
    public const string WhatIsSent =
        "A transmission names the address it goes to as uri, the text it sends as text (empty to send nothing and "
        + "ask with GET) and the character set the text is sent in as charset, EUC-JP or UTF-8.";

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<DataBroadcastTransmissionResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<DataBroadcastTransmissionResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<DataBroadcastTransmissionResponder>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<BaseResponder<DataBroadcastTransmissionResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromBody] TransmitDataBroadcastRequest? request,
        CancellationToken cancellationToken)
    {
        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return BadRequest(BaseResponder<DataBroadcastTransmissionResponder>.Error(LiveDataBroadcastChannel.TheChannelsThereAre));
        }

        if (request is not { Uri: { } uri, Charset: { } charset })
        {
            return BadRequest(BaseResponder<DataBroadcastTransmissionResponder>.Error(WhatIsSent));
        }

        ServiceResult<RelayedText, DataBroadcastRelayRefusal> relayed = await relay.TransmitAsync(
            channel.Network,
            channel.Service,
            DataBroadcastLineCookie.AllowsTheLine(Request.Cookies),
            uri,
            request.Text ?? string.Empty,
            charset,
            cancellationToken);

        return relayed.IsSuccess
            ? Ok(BaseResponder<DataBroadcastTransmissionResponder>.Success(DataBroadcastTransmissionResponder.Of(relayed.Data!)))
            : StatusCode(
                LiveDataBroadcastChannel.StatusOf(relayed.ErrorType),
                BaseResponder<DataBroadcastTransmissionResponder>.Error(relayed.ErrorMessage!));
    }
}

/// <summary>
/// The live channel a data broadcast relay is asked for, read from the path, and how a refusal of the relay is
/// answered.
/// </summary>
public sealed record LiveDataBroadcastChannel(NetworkId Network, ServiceId Service)
{
    public const string TheChannelsThereAre =
        "A live channel is named by a networkId and a serviceId, each a whole number in the range a broadcast carries.";

    public static LiveDataBroadcastChannel? Read(int networkId, int serviceId)
        => networkId is < NetworkId.MinValue or > NetworkId.MaxValue || serviceId is < ServiceId.MinValue or > ServiceId.MaxValue
            ? null
            : new LiveDataBroadcastChannel(new NetworkId(networkId), new ServiceId(serviceId));

    public static int StatusOf(DataBroadcastRelayRefusal refusal) => refusal switch
    {
        DataBroadcastRelayRefusal.NotAllowed => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status404NotFound,
    };
}
