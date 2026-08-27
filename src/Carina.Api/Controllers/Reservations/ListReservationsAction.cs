using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListReservationsAction(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ReservationListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] ReservationStanding[]? standing,
        [FromQuery] ReservationOrigin? origin,
        [FromQuery] string[]? channel,
        [FromQuery] string? keyword,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] ReservationSort sort,
        [FromQuery] bool descending,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        ReservationQuery? asked = ProgrammeServiceText.Every(channel) is { } channels
            ? ReservationQuery.For(
                from?.UtcDateTime,
                to?.UtcDateTime,
                sort,
                descending,
                page,
                perPage,
                new ReservationConditions
                {
                    Standings = standing,
                    Origin = origin,
                    Channels = channels,
                    Keyword = keyword,
                })
            : null;

        if (asked is null)
        {
            return BadRequest(BaseResponder<ReservationListResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<Reservation>> found = await reservations.ListAsync(asked, cancellationToken);

        return Ok(BaseResponder<ReservationListResponder>.Success(
            ReservationListResponder.Of(found.Data!)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{ReservationQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + $"A span runs forwards and reaches at most {ReservationQuery.LongestSpan.TotalDays:0} days, "
            + $"at most {ReservationQuery.MostChannels} channels are named as network-service, a keyword is at "
            + $"least {ReservationQuery.ShortestKeyword} characters, and the standing, the origin and the sort "
            + "are each one of the values this endpoint names.";
}
