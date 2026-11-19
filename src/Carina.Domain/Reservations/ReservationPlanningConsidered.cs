using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

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
