using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan-runs")]
public sealed class ListScanRunsAction(ScanService scanService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IReadOnlyList<ScanRunResponder>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        var result = await scanService.ListAsync(cancellationToken);

        return Ok(BaseResponder<IReadOnlyList<ScanRunResponder>>.Success(
            [.. result.Data!.Select(ScanRunResponder.Of)]));
    }
}
