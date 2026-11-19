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
