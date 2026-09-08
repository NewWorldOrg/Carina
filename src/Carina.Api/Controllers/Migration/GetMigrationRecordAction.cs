using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Migration;
using Carina.Api.Services;
using Carina.Domain.Migration;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Migration;

[ApiController]
[Route("api/migration/record")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetMigrationRecordAction(MigrationRecordService records) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<MigrationRecordResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<MigrationRecordResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        if (MigrationDetailQuery.For(page, perPage) is not { } asked)
        {
            return BadRequest(BaseResponder<MigrationRecordResponder>.Error(Refusal));
        }

        ServiceResult<MigrationRecordRead> read = await records.ReadAsync(asked, cancellationToken);

        return Ok(BaseResponder<MigrationRecordResponder>.Success(MigrationRecordResponder.Of(read.Data!)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{MigrationDetailQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + "The record answered is that of the most recent run, and there is none at all until one has run. "
            + "Every line of what a run did not carry is reachable by walking the pages, and nothing is left "
            + "out of the count.";
}
