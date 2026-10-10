using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What decides whether it is spare time for the heavy work of learning: whether learning is on,
/// whether anything is being recorded or watched, and when the nearest reservation still to be
/// recorded starts, its margin included.
/// </summary>
public sealed record Occupancy(bool Learning, bool Recording, bool Watching, DateTime? NextReservationStartsAt);

public enum SpareTimeVerdict
{
    Spare = 1,

    LearningOff = 2,

    Recording = 3,

    Watching = 4,

    ReservationSoon = 5,
}

/// <summary>
/// Whether it is spare time for the heavy work of learning: learning is on and the machine is idle as
/// <see cref="Idleness"/> judges it for every pass over ended recordings. Otherwise, the first of those that
/// stands in the way.
/// </summary>
public static class SpareTime
{
    public static readonly TimeSpan NoReservationWithin = Idleness.NoReservationWithin;

    public static SpareTimeVerdict Judge(Occupancy occupancy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(occupancy);

        IdleVerdict idle = Idleness.Judge(new Busyness(occupancy.Recording, occupancy.Watching, occupancy.NextReservationStartsAt), now);

        if (!occupancy.Learning)
        {
            return SpareTimeVerdict.LearningOff;
        }

        return idle switch
        {
            IdleVerdict.Recording => SpareTimeVerdict.Recording,
            IdleVerdict.Watching => SpareTimeVerdict.Watching,
            IdleVerdict.ReservationSoon => SpareTimeVerdict.ReservationSoon,
            _ => SpareTimeVerdict.Spare,
        };
    }
}
