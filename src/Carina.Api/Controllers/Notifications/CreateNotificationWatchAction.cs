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
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateNotificationWatchAction(NotificationWatchService watches) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<NotificationWatchResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke([FromBody] CreateNotificationWatchRequest? request, CancellationToken cancellationToken)
    {
        if (ProgrammeIdText.Read(request?.Programme) is not { } programme)
        {
            return BadRequest(BaseResponder<NotificationWatchResponder>.Error(
                "programme: a broadcast is named by network, service and event, as in 32736-1024-4001."));
        }

        if (request?.ProgrammeStartsAt is not { } startsAt)
        {
            return BadRequest(BaseResponder<NotificationWatchResponder>.Error(
                "programmeStartsAt: an event id is reused, so the start the broadcast was announced for is part "
                + "of naming it and is asked for beside the three numbers."));
        }

        ServiceResult<ProgrammeWatch, NotificationFailure> asked = await watches.AskAsync(
            new ProgrammeWatchAsked(programme, startsAt.UtcDateTime, request.LeadMinutes, request.Destinations),
            cancellationToken);

        return asked.IsSuccess
            ? Created(
                new Uri("/api/notifications/watches", UriKind.Relative),
                BaseResponder<NotificationWatchResponder>.Success(NotificationWatchResponder.Of(asked.Data!)))
            : StatusCode(NotificationStatus.Of(asked.ErrorType), BaseResponder<NotificationWatchResponder>.Error(asked.ErrorMessage!));
    }
}
