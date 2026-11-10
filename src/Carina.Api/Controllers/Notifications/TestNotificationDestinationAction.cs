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
[Route("api/notifications/destinations/{id:guid}/test")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class TestNotificationDestinationAction(NotificationDestinationService destinations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<NoticeTrialResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NoticeTrialResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NoticeTrialResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<NoticeTrialResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<NoticeTrialResponder>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Invoke(Guid id, CancellationToken cancellationToken)
    {
        if (NotificationIdText.Destination(id) is not { } destinationId)
        {
            return BadRequest(BaseResponder<NoticeTrialResponder>.Error(NotificationIdText.Description));
        }

        ServiceResult<NoticeTrialOutcome, NotificationFailure> tried = await destinations.TryAsync(destinationId, cancellationToken);

        return tried.IsSuccess
            ? Ok(BaseResponder<NoticeTrialResponder>.Success(NoticeTrialResponder.Of(tried.Data!)))
            : StatusCode(NotificationStatus.Of(tried.ErrorType), BaseResponder<NoticeTrialResponder>.Error(tried.ErrorMessage!));
    }
}
