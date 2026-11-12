using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/push/subscriptions/{destinationId:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReplacePushSubscriptionAction(NotificationPushService push) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(
        Guid destinationId,
        [FromBody] ReplacePushSubscriptionRequest? request,
        CancellationToken cancellationToken)
    {
        if (NotificationIdText.Destination(destinationId) is not { } id)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error(NotificationIdText.Description));
        }

        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error("A browser is given what it subscribed again."));
        }

        ServiceResult<ListedNotificationDestination, NotificationFailure> replaced = await push.ResubscribeAsync(
            id,
            request.Endpoint,
            request.Keys?.P256dh,
            request.Keys?.Auth,
            cancellationToken);

        return replaced.IsSuccess
            ? Ok(BaseResponder<NotificationDestinationResponder>.Success(NotificationDestinationResponder.Of(replaced.Data!)))
            : StatusCode(NotificationStatus.Of(replaced.ErrorType), BaseResponder<NotificationDestinationResponder>.Error(replaced.ErrorMessage!));
    }
}
