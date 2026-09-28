using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Scans;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan/{scanId:guid}")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetScanAction(ScanService scanService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ScanProgressResponder>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Invoke(Guid scanId, CancellationToken cancellationToken)
    {
        if (ScanIdText.Read(scanId) is not { } named)
        {
            return BadRequest(BaseResponder<ScanProgressResponder>.Error(ScanIdText.Description));
        }

        ServiceResult<ScanProgress, ScanFailure> result = await scanService.ProgressAsync(named, cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                ScanStatus.Of(result.ErrorType),
                BaseResponder<ScanProgressResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<ScanProgressResponder>.Success(ScanProgressResponder.Of(result.Data!)));
    }
}
