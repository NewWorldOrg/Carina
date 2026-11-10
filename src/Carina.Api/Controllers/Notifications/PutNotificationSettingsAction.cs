using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/settings")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PutNotificationSettingsAction(NotificationSettingsService settings) : ControllerBase
{
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationSettingsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationSettingsResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] PutNotificationSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<NotificationSettingsRead, NotificationFailure> changed = await settings.ChangeAsync(
            request?.NotifyLeadMinutes,
            cancellationToken);

        return changed.IsSuccess
            ? Ok(BaseResponder<NotificationSettingsResponder>.Success(NotificationSettingsResponder.Of(changed.Data!)))
            : StatusCode(NotificationStatus.Of(changed.ErrorType), BaseResponder<NotificationSettingsResponder>.Error(changed.ErrorMessage!));
    }
}
