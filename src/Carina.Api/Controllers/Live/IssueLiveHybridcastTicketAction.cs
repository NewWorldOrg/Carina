using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

/// <summary>
/// Starts the Hybridcast application a live channel's data broadcast document asks for: the AIT is read again, and
/// where it names an application by both identifiers that is to start by itself, a ticket is issued for it. The
/// frame the application runs in carries the ticket in the path of the relay, because it has no cookie of its own;
/// the relay then keeps to the application's boundary. Refused 403 unless the browser asking allows both
/// communication and Hybridcast, 404 where no application of the AIT is to be started by these identifiers, 429
/// where too many tickets are outstanding, and 502 where no AIT could be read.
/// </summary>
[ApiController]
[Route("api/live/{networkId:int}-{serviceId:int}/data-broadcast/app/ticket")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class IssueLiveHybridcastTicketAction(DataBroadcastRelayService relay) : ControllerBase
{
    public const string WhatStartsAnApplication =
        "An application is started by the address of its AIT as ait, its organisation identifier as orgId (1 or more), "
        + "its application identifier as appId (0 to 65535), both as numbers, and the path on this server of the "
        + "receiver script its documents are given as receiverScript.";

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<BaseResponder<HybridcastTicketResponder>>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromBody] StartHybridcastRequest? request,
        CancellationToken cancellationToken)
    {
        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return BadRequest(BaseResponder<HybridcastTicketResponder>.Error(LiveDataBroadcastChannel.TheChannelsThereAre));
        }

        if (Asked(request) is not { } ask)
        {
            return BadRequest(BaseResponder<HybridcastTicketResponder>.Error(WhatStartsAnApplication));
        }

        if (SessionClaims.SubjectOf(User) is not { } viewer)
        {
            return Unauthorized(BaseResponder<HybridcastTicketResponder>.Error("This request carries no session."));
        }

        ServiceResult<HybridcastStart, DataBroadcastRelayRefusal> started = await relay.StartApplicationAsync(
            channel.Network,
            channel.Service,
            DataBroadcastLineCookie.AllowsTheLine(Request.Cookies),
            DataBroadcastHybridcastCookie.AllowsHybridcast(Request.Cookies),
            viewer,
            ask,
            cancellationToken);

        if (!started.IsSuccess)
        {
            return StatusCode(
                LiveDataBroadcastChannel.StatusOf(started.ErrorType),
                BaseResponder<HybridcastTicketResponder>.Error(started.ErrorMessage!));
        }

        HybridcastStart start = started.Data!;
        string address = HybridcastPages.Address(
            HybridcastAppDelivery.RelayOf(networkId, serviceId, start.Ticket.InTheClear),
            start.Application.EntryUrl);

        return Ok(BaseResponder<HybridcastTicketResponder>.Success(HybridcastTicketResponder.Of(start, address)));
    }

    private static HybridcastAsk? Asked(StartHybridcastRequest? request)
    {
        if (request is not { Ait: { Length: > 0 } ait, OrgId: >= 1 and <= uint.MaxValue, AppId: >= 0 and <= ushort.MaxValue }
            || !DataBroadcastRelayService.IsReceiverScriptPath(request.ReceiverScript))
        {
            return null;
        }

        return new HybridcastAsk(ait, (uint)request.OrgId.Value, (ushort)request.AppId.Value, request.ReceiverScript!);
    }
}
