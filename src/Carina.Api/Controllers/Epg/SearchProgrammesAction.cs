using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Epg;

[ApiController]
[Route("api/programs/search")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class SearchProgrammesAction(ProgrammeGuideService guide) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ProgrammeSearchResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ProgrammeSearchResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] string? keyword,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] ProgrammeSort sort,
        [FromQuery] bool descending,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ProgrammeSearch? asked = ProgrammeSearch.For(
            keyword,
            from?.UtcDateTime,
            to?.UtcDateTime,
            sort,
            descending,
            page,
            perPage);

        if (asked is null)
        {
            return BadRequest(BaseResponder<ProgrammeSearchResponder>.Error(
                $"A search needs at least {ProgrammeSearch.ShortestKeyword} letters and a span of at most "
                + $"{ProgrammeSearch.LongestSpan.TotalDays:0} days that runs forwards."));
        }

        ServiceResult<PaginatedList<Programme>> found = await guide.SearchAsync(asked, cancellationToken);

        return Ok(BaseResponder<ProgrammeSearchResponder>.Success(
            ProgrammeSearchResponder.Of(found.Data!)));
    }
}
