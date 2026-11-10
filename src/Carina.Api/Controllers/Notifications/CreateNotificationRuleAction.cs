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
[Route("api/notifications/rules")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateNotificationRuleAction(NotificationRuleService rules) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke([FromBody] CreateNotificationRuleRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationRuleResponder>.Error("A set of rules is made of a name, the kinds it tells of and the destinations it tells."));
        }

        ServiceResult<NotificationRule, NotificationFailure> made = await rules.DefineAsync(
            new NotificationRuleChange(request.Name, request.Enabled, request.Events, request.Destinations),
            cancellationToken);

        return made.IsSuccess
            ? Created(
                new Uri("/api/notifications/rules", UriKind.Relative),
                BaseResponder<NotificationRuleResponder>.Success(NotificationRuleResponder.Of(made.Data!)))
            : StatusCode(NotificationStatus.Of(made.ErrorType), BaseResponder<NotificationRuleResponder>.Error(made.ErrorMessage!));
    }
}
