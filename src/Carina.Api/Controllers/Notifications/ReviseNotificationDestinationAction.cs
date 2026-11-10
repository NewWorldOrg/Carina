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
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseNotificationDestinationAction(NotificationDestinationService destinations) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] ReviseNotificationDestinationRequest? request,
        CancellationToken cancellationToken)
    {
        if (NotificationIdText.Destination(id) is not { } destinationId)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error(NotificationIdText.Description));
        }

        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error("A destination is changed by the name, the address or the switch it is given."));
        }

        ServiceResult<ListedNotificationDestination, NotificationFailure> revised = await destinations.ReviseAsync(
            destinationId,
            new NotificationDestinationChange(request.Name, request.Url, request.Enabled),
            cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<NotificationDestinationResponder>.Success(NotificationDestinationResponder.Of(revised.Data!)))
            : StatusCode(NotificationStatus.Of(revised.ErrorType), BaseResponder<NotificationDestinationResponder>.Error(revised.ErrorMessage!));
    }
}
