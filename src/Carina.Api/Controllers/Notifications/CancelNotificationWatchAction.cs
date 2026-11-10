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
[Route("api/notifications/watches/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CancelNotificationWatchAction(NotificationWatchService watches) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (NotificationIdText.Watch(id) is not { } watchId)
        {
            return BadRequest(BaseResponder<NotificationWatchResponder>.Error(NotificationIdText.Description));
        }

        ServiceResult<ProgrammeWatch, NotificationFailure> withdrawn = await watches.WithdrawAsync(watchId, cancellationToken);

        return withdrawn.IsSuccess
            ? Ok(BaseResponder<NotificationWatchResponder>.Success(NotificationWatchResponder.Of(withdrawn.Data!)))
            : StatusCode(NotificationStatus.Of(withdrawn.ErrorType), BaseResponder<NotificationWatchResponder>.Error(withdrawn.ErrorMessage!));
    }
}
