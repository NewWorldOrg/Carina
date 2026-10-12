using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Playback;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Domain.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

/// <summary>
/// Reads the AIT a live channel's data broadcast document names, from the broadcaster's server, and hands back the
/// applications in it this receiver can run. Refused 403 unless the browser asking allows both communication and
/// Hybridcast. An address the relay refuses, a server that could not be reached and an answer that is not an AIT
/// are answered 502, with the result a transmission would have had and the status the server answered. Opened with
/// the reader's own session only, never with a ticket.
/// </summary>
[ApiController]
[Route("api/live/{networkId:int}-{serviceId:int}/data-broadcast/ait")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetLiveDataBroadcastAitAction(DataBroadcastRelayService relay) : ControllerBase
{
    public const string WhatIsRead = "An AIT is read by naming its address as uri.";

    [HttpGet]
    [ProducesResponseType<BaseResponder<HybridcastAitResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<HybridcastAitResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<HybridcastAitResponder>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<BaseResponder<HybridcastAitResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<DataBroadcastFetchFailureResponder>>(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Invoke(
        int networkId,
        int serviceId,
        [FromQuery] string? uri,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = PlayDelivery.NeverCached;

        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return BadRequest(BaseResponder<HybridcastAitResponder>.Error(LiveDataBroadcastChannel.TheChannelsThereAre));
        }

        if (string.IsNullOrEmpty(uri))
        {
            return BadRequest(BaseResponder<HybridcastAitResponder>.Error(WhatIsRead));
        }

        ServiceResult<RelayedAit, DataBroadcastRelayRefusal> read = await relay.ReadAitAsync(
            channel.Network,
            channel.Service,
            DataBroadcastLineCookie.AllowsTheLine(Request.Cookies),
            DataBroadcastHybridcastCookie.AllowsHybridcast(Request.Cookies),
            uri,
            cancellationToken);

        if (!read.IsSuccess)
        {
            return StatusCode(
                LiveDataBroadcastChannel.StatusOf(read.ErrorType),
                BaseResponder<HybridcastAitResponder>.Error(read.ErrorMessage!));
        }

        RelayedAit answered = read.Data!;

        if (answered.Applications is { } applications)
        {
            return Ok(BaseResponder<HybridcastAitResponder>.Success(HybridcastAitResponder.Of(applications)));
        }

        return StatusCode(
            StatusCodes.Status502BadGateway,
            new BaseResponder<DataBroadcastFetchFailureResponder>(
                false,
                DataBroadcastRelayService.NoAitWasRead,
                new DataBroadcastFetchFailureResponder((int)answered.Result, answered.HttpStatus)));
    }
}
