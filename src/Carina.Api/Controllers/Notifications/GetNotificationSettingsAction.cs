using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/settings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetNotificationSettingsAction(NotificationSettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<NotificationSettingsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<NotificationSettingsRead> read = await settings.ReadAsync(cancellationToken);

        return Ok(BaseResponder<NotificationSettingsResponder>.Success(NotificationSettingsResponder.Of(read.Data!)));
    }
}
