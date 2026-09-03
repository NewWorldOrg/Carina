using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Live;
using Carina.Api.Services;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/profiles")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListLiveProfilesAction(LiveService live) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<LiveProfileResponder>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<LiveProfile>> listed = live.ListProfiles();
        ServiceResult<LiveProfile> unasked = await live.ProfileUnaskedAsync(cancellationToken);

        return Ok(BaseResponder<IReadOnlyList<LiveProfileResponder>>.Success(
            [.. listed.Data!.Select(profile => LiveProfileResponder.Of(profile, unasked.Data!))]));
    }
}
