using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Integrity;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/integrity/run")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class RunRecordingIntegrityCheckAction(IntegrityService integrity) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<IntegritySweepResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<IntegritySweepRefusedResponder>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<IntegritySweep> asked = await integrity.RunAsync(cancellationToken);
        IntegritySweep sweep = asked.Data!;

        if (sweep.Swept is { } swept)
        {
            return Ok(BaseResponder<IntegritySweepResponder>.Success(
                IntegritySweepResponder.Of(swept, sweep.Findings)));
        }

        return Conflict(new BaseResponder<IntegritySweepRefusedResponder>(
            false,
            Because(sweep.Verdict),
            IntegritySweepRefusedResponder.Of(sweep.Verdict)));
    }

    private static string Because(SweepVerdict verdict)
        => verdict.Refusal switch
        {
            SweepRefusal.OneIsAlreadyRunning =>
                "A check of the ledger against the files is already walking; only one runs at a time.",
            SweepRefusal.TooSoonAfterTheLastOne =>
                "The last check finished too recently to ask for another. Reading every recording off the disk "
                + "is not free, so a check asked for by hand waits for the one before it to age.",
            _ => "The check could not be started.",
        };
}
