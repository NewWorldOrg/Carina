using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/jobs")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListEncodeJobsAction(EncodeJobService jobs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeJobListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeJobListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] EncodeJobStatus[]? status,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        if (EncodeJobQuery.For(status, page, perPage) is not { } asked)
        {
            return BadRequest(BaseResponder<EncodeJobListResponder>.Error(
                "A page is asked for by a page number of at least 1, a page size above "
                + $"{EncodeJobQuery.MostPerPage} is cut down to it and answered as the size that was used, and each "
                + "status is one of the five the ledger holds."));
        }

        ServiceResult<PaginatedList<EncodeJobView>> found = await jobs.ListAsync(asked, cancellationToken);

        return Ok(BaseResponder<EncodeJobListResponder>.Success(EncodeJobListResponder.Of(found.Data!)));
    }
}
