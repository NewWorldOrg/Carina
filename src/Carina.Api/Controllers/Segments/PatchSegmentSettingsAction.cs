using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;
using Carina.Domain.Segments;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Segments;

[ApiController]
[Route("api/segments/settings")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class PatchSegmentSettingsAction(SegmentSettingsService settings) : ControllerBase
{
    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<BaseResponder<SegmentSettingsResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<SegmentSettingsResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromBody] PatchSegmentSettingsRequest? request,
        CancellationToken cancellationToken)
    {
        ServiceResult<SegmentSettingsStanding> changed = await settings.ChangeAsync(request?.Learning, cancellationToken);

        return changed.Data is { } standing
            ? Ok(BaseResponder<SegmentSettingsResponder>.Success(SegmentSettingsResponder.Of(standing)))
            : BadRequest(BaseResponder<SegmentSettingsResponder>.Error(changed.ErrorMessage!));
    }
}
