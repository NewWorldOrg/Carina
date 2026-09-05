using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations/outcomes")]
[EndpointEffect(EndpointEffect.Reading)]
public sealed class ListReservationOutcomesAction(ReservationService reservations) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BaseResponder<ReservationOutcomeListResponder>>(StatusCodes.Status200OK)]
    [ProducesResponseType<BaseResponder<ReservationOutcomeListResponder>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Invoke(
        [FromQuery] ReservationOutcomeKind[]? kind,
        [FromQuery] string[]? channel,
        [FromQuery] Guid? rule,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? page,
        [FromQuery] int? perPage,
        CancellationToken cancellationToken)
    {
        RuleId? byRule = rule is { } named ? RuleIdText.Read(named) : null;

        ReservationOutcomeQuery? asked =
            (rule is null || byRule is not null) && ProgrammeServiceText.Every(channel) is { } channels
                ? ReservationOutcomeQuery.For(
                    from?.UtcDateTime,
                    to?.UtcDateTime,
                    page,
                    perPage,
                    new ReservationOutcomeConditions
                    {
                        Kinds = kind,
                        Channels = channels,
                        Rule = byRule,
                    })
                : null;

        if (asked is null)
        {
            return BadRequest(BaseResponder<ReservationOutcomeListResponder>.Error(Refusal));
        }

        ServiceResult<PaginatedList<ReservationOutcome>> found =
            await reservations.ListOutcomesAsync(asked, cancellationToken);

        return Ok(BaseResponder<ReservationOutcomeListResponder>.Success(
            ReservationOutcomeListResponder.Of(found.Data!)));
    }

    private static string Refusal
        => "A page is asked for by a page number of at least 1, and a page size above "
            + $"{ReservationOutcomeQuery.MostPerPage} is cut down to it and answered as the size that was used. "
            + $"A span runs forwards and reaches at most {ReservationOutcomeQuery.LongestSpan.TotalDays:0} days, "
            + $"at most {ReservationOutcomeQuery.MostChannels} channels are named as network-service, a kind is one "
            + "of the values this endpoint names, and a rule is named by a UUID that is not all zeroes.";
}
