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
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateNotificationDestinationAction(NotificationDestinationService destinations) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<NotificationDestinationResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke([FromBody] CreateNotificationDestinationRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationDestinationResponder>.Error("A destination is registered by its name and the address of its webhook."));
        }

        ServiceResult<ListedNotificationDestination, NotificationFailure> registered =
            await destinations.RegisterAsync(request.Name, request.Url, cancellationToken);

        return registered.IsSuccess
            ? Created(
                new Uri("/api/notifications/destinations", UriKind.Relative),
                BaseResponder<NotificationDestinationResponder>.Success(NotificationDestinationResponder.Of(registered.Data!)))
            : StatusCode(
                NotificationStatus.Of(registered.ErrorType),
                BaseResponder<NotificationDestinationResponder>.Error(registered.ErrorMessage!));
    }
}
