using Carina.Api.Services;

namespace Carina.Api.Controllers.Reservations;

public static class ReservationStatus
{
    public static int Of(ReservationFailure failure) => failure switch
    {
        ReservationFailure.NoSuchReservation => StatusCodes.Status404NotFound,
        ReservationFailure.NoSuchProgramme => StatusCodes.Status404NotFound,
        ReservationFailure.TunersCannotBeCounted => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status409Conflict,
    };
}
