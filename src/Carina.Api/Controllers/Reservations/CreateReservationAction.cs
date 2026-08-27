using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Requests;
using Carina.Api.Responder;
using Carina.Api.Responder.Reservations;
using Carina.Api.Services;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

using Microsoft.AspNetCore.Mvc;

namespace Carina.Api.Controllers.Reservations;

[ApiController]
[Route("api/reservations")]
[EndpointEffect(EndpointEffect.Changing)]
public sealed class CreateReservationAction(ReservationService reservations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status201Created)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<BaseResponder<ReservationSettlementResponder>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Invoke(
        [FromBody] CreateReservationRequest? request,
        CancellationToken cancellationToken)
    {
        if (ProgrammeIdText.Read(request?.Programme) is not { } programme)
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(
                "programme: a broadcast is named by network, service and event, as in 32736-1024-4001."));
        }

        if (request?.ProgrammeStartsAt is not { } startsAt)
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(
                "programmeStartsAt: an event id is reused, so the start the broadcast was announced for is part "
                + "of naming it and is asked for beside the three numbers."));
        }

        if (!ReservationInput.Holds(request.Priority, request.MarginBeforeSeconds, request.MarginAfterSeconds))
        {
            return BadRequest(BaseResponder<ReservationSettlementResponder>.Error(ReservationInput.Description));
        }

        var draft = new ReservationDraft(
            programme,
            startsAt.UtcDateTime,
            ReservationInput.PriorityOf(request.Priority) ?? Priority.Default,
            ReservationInput.MarginOf(request.MarginBeforeSeconds) ?? Margin.None,
            ReservationInput.MarginOf(request.MarginAfterSeconds) ?? Margin.None);

        ServiceResult<ReservationSettlement, ReservationFailure> made =
            await reservations.CreateAsync(draft, cancellationToken);

        return made.IsSuccess
            ? Created(
                new Uri($"/api/reservations/{made.Data!.Reservation.Id.Value}", UriKind.Relative),
                BaseResponder<ReservationSettlementResponder>.Success(
                    ReservationSettlementResponder.Of(made.Data!)))
            : StatusCode(
                ReservationStatus.Of(made.ErrorType),
                BaseResponder<ReservationSettlementResponder>.Error(made.ErrorMessage!));
    }
}
