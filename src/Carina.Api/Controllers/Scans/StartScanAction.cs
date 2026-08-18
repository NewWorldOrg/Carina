using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Scans;
using Carina.Api.Services;
using Carina.Domain.Scans;
using Carina.Infrastructure.Scanning;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Scans;

[ApiController]
[Route("api/tuners/scan")]
public sealed class StartScanAction(ScanService scanService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ScanStartedResponder>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<BaseResponder<ScanStartedResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ScanRefusedResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ScanStartedResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        [FromBody] StartScanRequest? request,
        CancellationToken cancellationToken)
    {
        ScanScope? scope = (request ?? new StartScanRequest()).ToScope(out string? problem);

        if (scope is null)
        {
            return BadRequest(BaseResponder<ScanStartedResponder>.Error(problem!));
        }

        ServiceResult<ScanLaunch> result = await scanService.StartAsync(scope, cancellationToken);
        ScanLaunch launch = result.Data!;

        if (launch.CouldNotStartBecause is { } refusal)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                BaseResponder<ScanStartedResponder>.Error(refusal));
        }

        if (!launch.WasStarted)
        {
            return Conflict(new BaseResponder<ScanRefusedResponder>(
                false,
                "A scan is already walking; only one runs at a time.",
                ScanRefusedResponder.Of(launch)));
        }

        return Accepted(BaseResponder<ScanStartedResponder>.Success(ScanStartedResponder.Of(launch)));
    }
}
