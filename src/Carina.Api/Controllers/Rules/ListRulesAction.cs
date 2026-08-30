using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Rules;
using Carina.Api.Services;
using Carina.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Rules;

[ApiController]
[Route("api/rules")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListRulesAction(RuleService rules) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<RuleListResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<Rule>> found = await rules.ListAsync(cancellationToken);

        return Ok(BaseResponder<RuleListResponder>.Success(RuleListResponder.Of(found.Data!)));
    }
}
