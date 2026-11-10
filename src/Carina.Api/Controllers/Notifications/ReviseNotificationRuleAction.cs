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
[Route("api/notifications/rules/{id:guid}")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class ReviseNotificationRuleAction(NotificationRuleService rules) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<NotificationRuleResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(
        Guid id,
        [FromBody] ReviseNotificationRuleRequest? request,
        CancellationToken cancellationToken)
    {
        if (NotificationIdText.Rule(id) is not { } ruleId)
        {
            return BadRequest(BaseResponder<NotificationRuleResponder>.Error(NotificationIdText.Description));
        }

        if (request is null)
        {
            return BadRequest(BaseResponder<NotificationRuleResponder>.Error("A set of rules is changed by the name, the switch, the kinds or the destinations it is given."));
        }

        ServiceResult<NotificationRule, NotificationFailure> revised = await rules.ReviseAsync(
            ruleId,
            new NotificationRuleChange(request.Name, request.Enabled, request.Events, request.Destinations),
            cancellationToken);

        return revised.IsSuccess
            ? Ok(BaseResponder<NotificationRuleResponder>.Success(NotificationRuleResponder.Of(revised.Data!)))
            : StatusCode(NotificationStatus.Of(revised.ErrorType), BaseResponder<NotificationRuleResponder>.Error(revised.ErrorMessage!));
    }
}
