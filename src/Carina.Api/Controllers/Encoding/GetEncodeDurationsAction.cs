using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

[ApiController]
[Route("api/encoding/jobs/durations")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class GetEncodeDurationsAction(EncodeJobService jobs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<EncodeDurationsResponder>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        ServiceResult<EncodeSpells> took = await jobs.RecentSpellsAsync(cancellationToken);

        return Ok(BaseResponder<EncodeDurationsResponder>.Success(EncodeDurationsResponder.Of(took.Data!)));
    }
}
