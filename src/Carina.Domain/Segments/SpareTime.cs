using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What decides whether it is spare time for the heavy work of learning: whether anything is being recorded or
/// watched, and when the nearest reservation still to be recorded starts, its margin included.
/// </summary>
public sealed record Occupancy(bool Recording, bool Watching, DateTime? NextReservationStartsAt);

public enum SpareTimeVerdict
{
    Spare = 1,

    Recording = 3,

    Watching = 4,

    ReservationSoon = 5,
}

/// <summary>
/// Whether it is spare time for the heavy work of learning: the machine is idle as <see cref="Idleness"/> judges it
/// for every pass over ended recordings. Otherwise, the first of those that stands in the way.
/// </summary>
public static class SpareTime
{
    public static readonly TimeSpan NoReservationWithin = Idleness.NoReservationWithin;

    public static SpareTimeVerdict Judge(Occupancy occupancy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(occupancy);

        return Idleness.Judge(new Busyness(occupancy.Recording, occupancy.Watching, occupancy.NextReservationStartsAt), now) switch
        {
            IdleVerdict.Recording => SpareTimeVerdict.Recording,
            IdleVerdict.Watching => SpareTimeVerdict.Watching,
            IdleVerdict.ReservationSoon => SpareTimeVerdict.ReservationSoon,
            _ => SpareTimeVerdict.Spare,
        };
    }
}
