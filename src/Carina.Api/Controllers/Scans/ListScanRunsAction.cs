using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Scans;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan-runs")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListScanRunsAction(ScanService scanService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<ScanRunResponder>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IReadOnlyList<ScanRun>> result = await scanService.ListAsync(cancellationToken);

        return Ok(BaseResponder<IReadOnlyList<ScanRunResponder>>.Success(
            [.. result.Data!.Select(ScanRunResponder.Of)]));
    }
}
