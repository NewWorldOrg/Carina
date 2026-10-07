using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Segments;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Segments;

[ApiController]
[Route("api/segments/status")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetSegmentStatusAction(SegmentStatusService status) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<SegmentStatusResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<SegmentStatus> read = await status.ReadAsync(cancellationToken);

        return Ok(BaseResponder<SegmentStatusResponder>.Success(SegmentStatusResponder.Of(read.Data!)));
    }
}
