using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Contracts;
using Carina.Domain.Programmes;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/programs")]
public sealed class GetProgrammeGuideAction(ProgrammeGuideService guide) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<GuideResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<GuideResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> Invoke(
        [FromQuery] TuneSystem? type,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (type is not { } system || system is TuneSystem.Unspecified)
        {
            return BadRequest(BaseResponder<GuideResponder>.Error(
                "A guide is read one broadcast type at a time; name which."));
        }

        if (from is not { } start || to is not { } end)
        {
            return BadRequest(BaseResponder<GuideResponder>.Error(
                "A guide is read over a window; name from and to."));
        }

        if (GuideWindow.Between(start.UtcDateTime, end.UtcDateTime) is not { } window)
        {
            return BadRequest(BaseResponder<GuideResponder>.Error(
                $"A guide window runs forwards and covers at most {GuideWindow.Longest.TotalDays:0} days."));
        }

        ServiceResult<GuidePage> read = await guide.ReadAsync(system, window, cancellationToken);
        GuidePage page = read.Data!;

        if (Request.Headers.IfNoneMatch.Contains(page.ETag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers[HeaderNames.ETag] = page.ETag;

        return Ok(BaseResponder<GuideResponder>.Success(GuideResponder.Of(page)));
    }
}
