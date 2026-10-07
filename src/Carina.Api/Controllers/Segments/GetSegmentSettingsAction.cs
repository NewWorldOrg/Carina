using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;
using Carina.Domain.Segments;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Segments;

[ApiController]
[Route("api/segments/settings")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetSegmentSettingsAction(SegmentSettingsService settings) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<SegmentSettingsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<SegmentSettingsStanding> read = await settings.ReadAsync(cancellationToken);

        return Ok(BaseResponder<SegmentSettingsResponder>.Success(SegmentSettingsResponder.Of(read.Data!)));
    }
}
