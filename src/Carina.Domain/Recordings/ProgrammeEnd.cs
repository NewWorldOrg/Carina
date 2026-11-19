namespace Carina.Domain.Recordings;

public static class ProgrammeEnd
{
    public static DateTime? Of(
        DateTime programmeStartsAt,
        DateTime? byTheGuide,
        DateTime? byTheReservation,
        bool reservationEndConfirmed,
        DateTime? promised)
        => new[] { byTheGuide, reservationEndConfirmed ? byTheReservation : null, promised }
            .FirstOrDefault(end => end > programmeStartsAt);
}
