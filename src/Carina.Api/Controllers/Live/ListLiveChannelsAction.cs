using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Live;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Streaming;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Live;

[ApiController]
[Route("api/live/channels")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListLiveChannelsAction(LiveService live) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<LiveChannelListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<LiveChannelListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] LiveChannelSort sort,
        [FromQuery] bool descending,
        [FromQuery] LiveChannelField[]? fields,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        if (LiveChannelQuery.For(sort, descending, fields, page, perPage) is not { } asked)
        {
            return BadRequest(BaseResponder<LiveChannelListResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<LiveChannelListing>> found = await live.ListChannelsAsync(asked, cancellationToken);

        return Ok(BaseResponder<LiveChannelListResponder>.Success(LiveChannelListResponder.Of(found.Data!, asked)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{LiveChannelQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + "The sort and each field asked for are one of the values this endpoint names.";
}
