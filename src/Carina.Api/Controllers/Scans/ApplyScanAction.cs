using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Scans;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan/{scanId:guid}/apply")]
public sealed class ApplyScanAction(ScanService scanService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ScanApplicationResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ScanApplicationResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ScanApplicationResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ScanApplicationResponder>>(StatusCodes.Status410Gone)]
    public async Task<IActionResult> Invoke(Guid scanId, CancellationToken cancellationToken)
    {
        var result = await scanService.ApplyAsync(new ScanRunId(scanId), cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                ScanStatus.Of(result.ErrorType),
                BaseResponder<ScanApplicationResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<ScanApplicationResponder>.Success(
            ScanApplicationResponder.Of(result.Data!)));
    }
}
