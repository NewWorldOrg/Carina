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
[Route("api/notifications/watches")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListNotificationWatchesAction(NotificationWatchService watches) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<NotificationWatchListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<ProgrammeWatch>> listed = await watches.ListAsync(cancellationToken);

        return Ok(BaseResponder<NotificationWatchListResponder>.Success(NotificationWatchListResponder.Of(listed.Data!)));
    }
}
