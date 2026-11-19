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
        DataBroadcastRelayRefusal.NotAllowed or DataBroadcastRelayRefusal.OutsideTheBoundary => StatusCodes.Status403Forbidden,
        DataBroadcastRelayRefusal.DriverUnreachable => StatusCodes.Status503ServiceUnavailable,
        DataBroadcastRelayRefusal.TooManyOutstanding => StatusCodes.Status429TooManyRequests,
        DataBroadcastRelayRefusal.AitNotRead => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status404NotFound,
    };
}
