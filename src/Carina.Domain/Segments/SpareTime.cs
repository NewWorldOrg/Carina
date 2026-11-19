using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed record Occupancy(bool Recording, bool Watching, DateTime? NextReservationStartsAt);

public enum SpareTimeVerdict
{
    Spare = 1,

    Recording = 3,

    Watching = 4,

    ReservationSoon = 5,
}

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
