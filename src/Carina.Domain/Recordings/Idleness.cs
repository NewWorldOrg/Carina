using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed record Busyness(bool Recording, bool Watching, DateTime? NextReservationStartsAt);

public enum IdleVerdict
{
    Idle = 1,

    Recording = 2,

    Watching = 3,

    ReservationSoon = 4,
}

public static class Idleness
{
    public static readonly TimeSpan NoReservationWithin = TimeSpan.FromMinutes(30);

    public static IdleVerdict Judge(Busyness busyness, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(busyness);

        DateTime at = UtcTimes.Required(now, nameof(now));

        if (busyness.Recording)
        {
            return IdleVerdict.Recording;
        }

        if (busyness.Watching)
        {
            return IdleVerdict.Watching;
        }

        return busyness.NextReservationStartsAt <= at + NoReservationWithin ? IdleVerdict.ReservationSoon : IdleVerdict.Idle;
    }
}

public interface IBusynessReader
{
    Task<Busyness> ReadAsync(DateTime now, CancellationToken cancellationToken);
}
