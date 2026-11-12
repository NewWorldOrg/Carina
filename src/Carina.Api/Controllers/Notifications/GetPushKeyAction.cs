using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/push/key")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetPushKeyAction(NotificationPushService push) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<PushKeyResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<PushKeyResponder>>(StatusCodes.Status409Conflict)]
    public IActionResult Invoke()
    {
        ServiceResult<PushKeyHeld, NotificationFailure> held = push.Key();

        return held.IsSuccess
            ? Ok(BaseResponder<PushKeyResponder>.Success(PushKeyResponder.Of(held.Data!)))
            : StatusCode(NotificationStatus.Of(held.ErrorType), BaseResponder<PushKeyResponder>.Error(held.ErrorMessage!));
    }
}
