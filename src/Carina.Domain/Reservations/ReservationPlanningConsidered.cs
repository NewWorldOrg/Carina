using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

/// <summary>
/// A programme asked for by hand, judged at <paramref name="At"/>: the programme the guide holds and how its broadcast
/// resolves when it may be reserved, or why it may not, with the primary listing of a moved broadcast or the
/// reservation already standing for it.
/// </summary>
public sealed record ReservationPlanningConsidered(
    Programme? Programme,
    BroadcastResolution? Resolution,
    ReservationPlanningRefusal? Refusal,
    Programme? Primary,
    Reservation? Already,
    DateTime At)
{
    public static ReservationPlanningConsidered Refused(
        ReservationPlanningRefusal refusal,
        DateTime at,
        Programme? primary = null,
        Reservation? already = null)
        => new(null, null, refusal, primary, already, at);
}
