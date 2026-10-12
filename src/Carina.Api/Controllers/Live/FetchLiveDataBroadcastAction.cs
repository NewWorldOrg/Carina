using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Live;
using Carina.Api.Playback;
using Carina.Api.Responder;
using Carina.Api.Responder.DataBroadcast;
using Carina.Api.Services;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

/// <summary>
/// Fetches a document or resource a live channel's data broadcast document asks for from a broadcaster's server and
/// hands it over as a module of the side channel is carried: the byte 0x02, a tag, module id and version of zero,
/// then the one resource with the address it was finally fetched from as its path, its kind and its bytes, text in
/// UTF-8. An address the relay refuses, a server that could not be reached and one that did not answer with a
/// success are answered 502, with the result a transmission would have had and the status the server answered. Opened with the reader's own session
/// only, never with a ticket.
/// </summary>
[ApiController]
[Route("api/live/{networkId:int}-{serviceId:int}/data-broadcast/fetch")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class FetchLiveDataBroadcastAction(DataBroadcastRelayService relay) : ControllerBase
{
    public const string WhatIsFetched = "A fetch names the address of the document or resource as uri.";

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<DataBroadcastFetchFailureResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<DataBroadcastFetchFailureResponder>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<BaseResponder<DataBroadcastFetchFailureResponder>>(StatusCodes.Status404NotFound)]
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
            return BadRequest(BaseResponder<DataBroadcastFetchFailureResponder>.Error(LiveDataBroadcastChannel.TheChannelsThereAre));
        }

        if (string.IsNullOrEmpty(uri))
        {
            return BadRequest(BaseResponder<DataBroadcastFetchFailureResponder>.Error(WhatIsFetched));
        }

        ServiceResult<RelayedResource, DataBroadcastRelayRefusal> relayed = await relay.FetchAsync(
            channel.Network,
            channel.Service,
            DataBroadcastLineCookie.AllowsTheLine(Request.Cookies),
            uri,
            cancellationToken);

        if (!relayed.IsSuccess)
        {
            return StatusCode(
                LiveDataBroadcastChannel.StatusOf(relayed.ErrorType),
                BaseResponder<DataBroadcastFetchFailureResponder>.Error(relayed.ErrorMessage!));
        }

        RelayedResource fetched = relayed.Data!;

        if (fetched.Resource is { } resource)
        {
            return File(
                DataBroadcastFrames.ModulePayload(new ModuleVersion(0, 0, 0, 0, 0, [resource])),
                DataBroadcastDelivery.MediaType);
        }

        return StatusCode(
            StatusCodes.Status502BadGateway,
            new BaseResponder<DataBroadcastFetchFailureResponder>(
                false,
                "The broadcaster's server did not hand the document over.",
                DataBroadcastFetchFailureResponder.Of(fetched)));
    }
}
