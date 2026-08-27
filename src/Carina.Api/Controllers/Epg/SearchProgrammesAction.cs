using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;

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
    public async Task<IActionResult> Invoke(CancellationToken cancellationToken)
    {
        if (ProgrammeSearchQuery.Read(Request.QueryString.Value) is not { } asked)
        {
            return BadRequest(BaseResponder<ProgrammeSearchResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<ProgrammeMatch>> found = await guide.SearchAsync(asked, cancellationToken);

        return Ok(BaseResponder<ProgrammeSearchResponder>.Success(
            ProgrammeSearchResponder.Of(found.Data!)));
    }

    private static string Refusal
        => "A search needs at least one condition that narrows: a keyword, a word to leave out, a genre, "
            + "a broadcast type, a channel, or an end of a span. Naming where to look narrows nothing on its own, "
            + "and neither does the sort or the page. "
            + $"A keyword, where one is given, carries a word of at least {ProgrammeSearch.ShortestKeyword} letters, "
            + $"at most {ProgrammeSearch.MostWords} words to look for and as many to leave out, "
            + $"genres between 0 and {ProgrammeSearch.HighestGenre}, "
            + $"at most {ProgrammeSearch.MostChannels} channels named as network-service, "
            + $"and a span of at most {ProgrammeSearch.LongestSpan.TotalDays:0} days that runs forwards.";
}
