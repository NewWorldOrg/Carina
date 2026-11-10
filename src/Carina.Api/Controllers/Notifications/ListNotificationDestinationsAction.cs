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
[Route("api/notifications/destinations")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListNotificationDestinationsAction(NotificationDestinationService destinations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<NotificationDestinationListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<ListedNotificationDestination>> listed = await destinations.ListAsync(cancellationToken);

        return Ok(BaseResponder<NotificationDestinationListResponder>.Success(NotificationDestinationListResponder.Of(listed.Data!)));
    }
}
