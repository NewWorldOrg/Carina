using Carina.Api.Services;

namespace Carina.Api.Responder.Reservations;

public sealed record ReservationDiscardResponder(Guid ReservationId)
{
    public static ReservationDiscardResponder Of(ReservationDiscarded discarded)
    {
        ArgumentNullException.ThrowIfNull(discarded);

        return new ReservationDiscardResponder(discarded.Id.Value);
    }
}

public sealed record ReservationDiscardRefusedResponder(Guid ReservationId, ReservationFailure Refusal)
{
    public static ReservationDiscardRefusedResponder Of(Guid reservationId, ReservationFailure refusal)
        => new(reservationId, refusal);
}
