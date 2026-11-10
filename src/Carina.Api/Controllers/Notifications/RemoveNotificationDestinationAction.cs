using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;
using Carina.Domain.Notifications;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/destinations/{id:guid}")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class RemoveNotificationDestinationAction(NotificationDestinationService destinations) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<NotificationRemovalResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationRemovalResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationRemovalResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (NotificationIdText.Destination(id) is not { } destinationId)
        {
            return BadRequest(BaseResponder<NotificationRemovalResponder>.Error(NotificationIdText.Description));
        }

        ServiceResult<NotificationRemoved, NotificationFailure> removed = await destinations.RemoveAsync(destinationId, cancellationToken);

        return removed.IsSuccess
            ? Ok(BaseResponder<NotificationRemovalResponder>.Success(NotificationRemovalResponder.Of(removed.Data!)))
            : StatusCode(NotificationStatus.Of(removed.ErrorType), BaseResponder<NotificationRemovalResponder>.Error(removed.ErrorMessage!));
    }
}
