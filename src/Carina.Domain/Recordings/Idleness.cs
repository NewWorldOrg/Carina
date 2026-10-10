using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

/// <summary>
/// What decides whether the machine is idle enough for heavy work on ended recordings: whether anything is
/// being recorded or watched, and when the nearest reservation still to be recorded starts, its margin included.
/// </summary>
public sealed record Busyness(bool Recording, bool Watching, DateTime? NextReservationStartsAt);

public enum IdleVerdict
{
    Idle = 1,

    Recording = 2,

    Watching = 3,

    ReservationSoon = 4,
}

/// <summary>
/// Whether the machine is idle enough for heavy work on ended recordings — taking their captions, their data
/// broadcast, or learning from them: nothing is being recorded or watched, and no reservation starts within
/// <see cref="NoReservationWithin"/>. Otherwise, the first of those that stands in the way.
/// </summary>
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

/// <summary>
/// Reads what decides whether the machine is idle enough for heavy work on ended recordings.
/// </summary>
public interface IBusynessReader
{
    Task<Busyness> ReadAsync(DateTime now, CancellationToken cancellationToken);
}
