namespace Carina.Domain.Recordings;

/// <summary>
/// When a recorded programme ends: when the guide says, else when its reservation says once that end was
/// confirmed, else where its recording was promised to end, taking only an end later than the programme starts.
/// </summary>
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
