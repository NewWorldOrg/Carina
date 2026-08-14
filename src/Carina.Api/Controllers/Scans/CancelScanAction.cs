using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Scans;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan/{scanId:guid}/cancel")]
public sealed class CancelScanAction(ScanService scanService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(Guid scanId, CancellationToken cancellationToken)
    {
        var result = await scanService.CancelAsync(new ScanRunId(scanId), cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                ScanStatus.Of(result.ErrorType),
                BaseResponder<ScanProgressResponder>.Error(result.ErrorMessage!));
        }

        return Accepted(BaseResponder<ScanProgressResponder>.Success(
            ScanProgressResponder.Of(result.Data!)));
    }
}
