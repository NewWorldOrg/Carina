using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/epg/rebuild")]
[EndpointEffect(EndpointEffect.Destructive)]
public sealed class RebuildEpgAction(EpgRebuildService rebuilds) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<EpgRebuiltResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EpgRebuiltResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] RebuildEpgRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.MeansIt is not true)
        {
            return BadRequest(BaseResponder<EpgRebuiltResponder>.Error(
                $"Discarding the whole guide needs confirm to say '{RebuildEpgRequest.TheWordThatMeansIt}'."));
        }

        ServiceResult<EpgRebuilt> rebuilt = await rebuilds.RebuildAsync(cancellationToken);

        return Ok(BaseResponder<EpgRebuiltResponder>.Success(EpgRebuiltResponder.Of(rebuilt.Data!)));
    }
}
