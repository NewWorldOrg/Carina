using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Recordings;
using Carina.Api.Services;
using Carina.Domain.Integrity;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Recordings;

[ApiController]
[Route("api/recordings/integrity")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetRecordingIntegrityAction(IntegrityService integrity) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<IntegrityListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<IntegrityListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        if (IntegrityFindingQuery.For(page, perPage) is not { } asked)
        {
            return BadRequest(BaseResponder<IntegrityListResponder>.Error(Refusal));
        }

        ServiceResult<IntegrityFindings> found = await integrity.ListAsync(asked, cancellationToken);

        return Ok(BaseResponder<IntegrityListResponder>.Success(
            IntegrityListResponder.Of(found.Data!.Check, found.Data!.Findings)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{IntegrityFindingQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + "The findings answered are those of the most recent check, and there are none at all until one "
            + "has run.";
}
