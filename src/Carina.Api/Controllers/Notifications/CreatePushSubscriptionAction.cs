using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/push/subscriptions")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreatePushSubscriptionAction(NotificationPushService push) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke([FromBody] CreatePushSubscriptionRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error("A browser is registered by its name and what it subscribed."));
        }

        ServiceResult<PushEnrolled, NotificationFailure> enrolled = await push.SubscribeAsync(
            request.Name,
            request.Endpoint,
            request.Keys?.P256dh,
            request.Keys?.Auth,
            cancellationToken);

        if (!enrolled.IsSuccess)
        {
            return StatusCode(
                NotificationStatus.Of(enrolled.ErrorType),
                BaseResponder<NotificationDestinationResponder>.Error(enrolled.ErrorMessage!));
        }

        BaseResponder<NotificationDestinationResponder> answer =
            BaseResponder<NotificationDestinationResponder>.Success(NotificationDestinationResponder.Of(enrolled.Data!.Destination));

        return enrolled.Data.Added
            ? Created(new Uri("/api/notifications/destinations", UriKind.Relative), answer)
            : Ok(answer);
    }
}
