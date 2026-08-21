using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Epg;
using Carina.Api.Services;
using Carina.Contracts;
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
        [FromQuery] string? exclude,
        [FromQuery] ProgrammeField[]? fields,
        [FromQuery] int[]? genre,
        [FromQuery] TuneSystem? type,
        [FromQuery] string[]? channel,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] ProgrammeSort sort,
        [FromQuery] bool descending,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ProgrammeSearch? asked = ProgrammeServiceText.Every(channel) is { } channels
            ? ProgrammeSearch.For(
                keyword,
                from?.UtcDateTime,
                to?.UtcDateTime,
                sort,
                descending,
                page,
                perPage,
                new ProgrammeConditions
                {
                    Exclude = exclude,
                    Fields = fields,
                    Genres = genre,
                    System = type,
                    Channels = channels,
                })
            : null;

        if (asked is null)
        {
            return BadRequest(BaseResponder<ProgrammeSearchResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<Programme>> found = await guide.SearchAsync(asked, cancellationToken);

        return Ok(BaseResponder<ProgrammeSearchResponder>.Success(
            ProgrammeSearchResponder.Of(found.Data!)));
    }

    private static string Refusal
        => $"A search needs a keyword carrying a word of at least {ProgrammeSearch.ShortestKeyword} letters, "
            + $"at most {ProgrammeSearch.MostWords} words to look for and as many to leave out, "
            + $"genres between 0 and {ProgrammeSearch.HighestGenre}, "
            + $"at most {ProgrammeSearch.MostChannels} channels named as network-service, "
            + $"and a span of at most {ProgrammeSearch.LongestSpan.TotalDays:0} days that runs forwards.";
}
