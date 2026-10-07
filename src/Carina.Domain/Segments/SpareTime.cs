using Carina.Domain.Base;

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
/// Whether it is spare time for the heavy work of learning: learning is on, nothing is being recorded or
/// watched, and no reservation starts within <see cref="NoReservationWithin"/>. Otherwise, the first of
/// those that stands in the way.
/// </summary>
public static class SpareTime
{
    public static readonly TimeSpan NoReservationWithin = TimeSpan.FromMinutes(30);

    public static SpareTimeVerdict Judge(Occupancy occupancy, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(occupancy);

        DateTime at = UtcTimes.Required(now, nameof(now));

        if (!occupancy.Learning)
        {
            return SpareTimeVerdict.LearningOff;
        }

        if (occupancy.Recording)
        {
            return SpareTimeVerdict.Recording;
        }

        if (occupancy.Watching)
        {
            return SpareTimeVerdict.Watching;
        }

        return occupancy.NextReservationStartsAt <= at + NoReservationWithin ? SpareTimeVerdict.ReservationSoon : SpareTimeVerdict.Spare;
    }
}
