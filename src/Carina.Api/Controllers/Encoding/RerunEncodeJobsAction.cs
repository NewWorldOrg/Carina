using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Encoding;
using Carina.Api.Services;
using Carina.Domain.Encodings;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Encoding;

/// <summary>
/// Runs failed jobs again, each by its id, up to <see cref="EncodeReruns.MostAtOnce"/> in one request.
/// Each id is taken on its own, through the same path a person queues one recording by hand, and the
/// answer holds one row per id in the order asked, so a refusal of one leaves the rest queued.
/// </summary>
[ApiController]
[Route("api/encoding/jobs/retry")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class RerunEncodeJobsAction(EncodeJobService jobs) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<EncodeRerunListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<EncodeRerunListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke([FromBody] RerunEncodeJobsRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Ids is not { Count: > 0 and <= EncodeReruns.MostAtOnce } asked)
        {
            return BadRequest(BaseResponder<EncodeRerunListResponder>.Error("ids: " + EncodeReruns.Description));
        }

        EncodeJobId[] ids = [.. asked.Select(EncodingIdText.Job).OfType<EncodeJobId>()];

        if (ids.Length != asked.Count)
        {
            return BadRequest(BaseResponder<EncodeRerunListResponder>.Error("ids: " + EncodingIdText.Description));
        }

        ServiceResult<IReadOnlyList<EncodeRerunRow>> rerun = await jobs.RerunAsync(ids, cancellationToken);

        return Ok(BaseResponder<EncodeRerunListResponder>.Success(EncodeRerunListResponder.Of(rerun.Data!)));
    }
}
