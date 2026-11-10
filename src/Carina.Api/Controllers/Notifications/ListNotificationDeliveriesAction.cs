using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Notifications;
using Carina.Api.Services;
using Carina.Domain.Notifications;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Notifications;

[ApiController]
[Route("api/notifications/deliveries")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListNotificationDeliveriesAction(NotificationDeliveryService deliveries) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<NotificationDeliveryListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationDeliveryListResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationDeliveryListResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        [FromQuery] Guid? destinationId,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        if (NotificationIdText.Destination(destinationId) is not { } destination)
        {
            return BadRequest(BaseResponder<NotificationDeliveryListResponder>.Error(Refusal));
        }

        ServiceResult<NotificationDeliveriesListed, NotificationFailure> listed = await deliveries.LatestOfAsync(
            destination,
            perPage,
            cancellationToken);

        return listed.IsSuccess
            ? Ok(BaseResponder<NotificationDeliveryListResponder>.Success(NotificationDeliveryListResponder.Of(listed.Data!)))
            : StatusCode(NotificationStatus.Of(listed.ErrorType), BaseResponder<NotificationDeliveryListResponder>.Error(listed.ErrorMessage!));
    }

    private static string Refusal
        => "The deliveries are asked for by the UUID of the destination they went to, and a page size above "
            + $"{NotificationDelivery.MostListed} is cut down to it and answered as the size that was used.";
}
