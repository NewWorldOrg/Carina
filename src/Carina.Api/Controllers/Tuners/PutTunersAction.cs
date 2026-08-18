using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Tuners;
using Carina.Api.Services;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Tuners;

[ApiController]
[Route("api/tuners")]
public sealed class PutTunersAction(TunerLedgerService tunerLedgerService) : ControllerBase
{
    [HttpPut]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status501NotImplemented)]
    [ProducesResponseType<BaseResponder<TunerLedgerResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        [FromBody] TunerLedgerRequest request,
        CancellationToken cancellationToken)
    {
        ServiceResult<TunerLedgerView, TunerLedgerFailure> result = await tunerLedgerService.ReplaceAsync(
            request?.ToEntries() ?? [],
            cancellationToken);

        if (!result.IsSuccess)
        {
            return StatusCode(
                TunerLedgerStatus.Of(result.ErrorType),
                BaseResponder<TunerLedgerResponder>.Error(result.ErrorMessage!));
        }

        return Ok(BaseResponder<TunerLedgerResponder>.Success(TunerLedgerResponder.Of(result.Data!)));
    }
}
