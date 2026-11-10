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
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListNotificationRulesAction(NotificationRuleService rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<NotificationRuleListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<NotificationRule>> listed = await rules.ListAsync(cancellationToken);

        return Ok(BaseResponder<NotificationRuleListResponder>.Success(NotificationRuleListResponder.Of(listed.Data!)));
    }
}
