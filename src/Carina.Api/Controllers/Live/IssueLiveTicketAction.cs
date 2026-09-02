using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Playback;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.Channels;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/ticket")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class IssueLiveTicketAction(LiveService live) : ControllerBase
{
    public const string TheChannelThereIs =
        "A ticket is asked for by a networkId and a serviceId, each a whole number in the range a broadcast carries.";

    [HttpPost]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<PlaybackTicketResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke([FromBody] LiveTicketRequest request, CancellationToken cancellationToken)
    {
        if (request is not { NetworkId: { } network, ServiceId: { } service }
            || network is < NetworkId.MinValue or > NetworkId.MaxValue
            || service is < ServiceId.MinValue or > ServiceId.MaxValue)
        {
            return BadRequest(BaseResponder<PlaybackTicketResponder>.Error(TheChannelThereIs));
        }

        if (SessionClaims.SubjectOf(User) is not { } watcher)
        {
            return Unauthorized(BaseResponder<PlaybackTicketResponder>.Error("This request carries no session."));
        }

        ServiceResult<IssuedPlaybackTicket, LiveTicketRefusal> issued = await live.IssueTicketAsync(
            new NetworkId(network),
            new ServiceId(service),
            watcher,
            cancellationToken);

        return issued.IsSuccess
            ? Ok(BaseResponder<PlaybackTicketResponder>.Success(PlaybackTicketResponder.Of(issued.Data!)))
            : StatusCode(
                Of(issued.ErrorType),
                BaseResponder<PlaybackTicketResponder>.Error(issued.ErrorMessage!));
    }

    private static int Of(LiveTicketRefusal refusal) => refusal switch
    {
        LiveTicketRefusal.NoSuchChannel => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status429TooManyRequests,
    };
}
