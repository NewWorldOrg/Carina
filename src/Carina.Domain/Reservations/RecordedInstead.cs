using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

/// <summary>
/// Which reservations a reservation lost to competition names as recorded in its place. The ledger is written at two
/// moments: when a start is refused because every tuner is taken, every recording then in flight was recorded instead;
/// when the window of a contended reservation closes, the claimed reservations whose windows met its own were.
/// </summary>
public static class RecordedInstead
{
    /// <summary>
    /// The claimed reservations whose effective windows meet that of <paramref name="lost"/>, earliest first.
    /// </summary>
    public static IReadOnlyList<Guid> Of(Reservation lost, IReadOnlyList<Reservation> claimed)
    {
        ArgumentNullException.ThrowIfNull(lost);
        ArgumentNullException.ThrowIfNull(claimed);

        return
        [
            .. claimed
                .Where(won => won.EffectiveStartAt < lost.EffectiveEndAt && lost.EffectiveStartAt < won.EffectiveEndAt)
                .OrderBy(won => won.EffectiveStartAt)
                .ThenBy(won => won.Id.Value)
                .Select(won => won.Id.Value),
        ];
    }

    /// <summary>
    /// The window the claims are looked for in: every lost reservation's window, widened by the longest margin either
    /// side, so a claim whose margins reach into it is found.
    /// </summary>
    public static ReservationWindow LookedForAcross(IReadOnlyList<Reservation> lost)
    {
        ArgumentNullException.ThrowIfNull(lost);

        if (lost.Count is 0)
        {
            throw new ArgumentException("Claims are looked for only for a reservation that was lost.", nameof(lost));
        }

        return new ReservationWindow(
            lost.Min(one => one.EffectiveStartAt) - Margin.Longest,
            lost.Max(one => one.EffectiveEndAt) + Margin.Longest);
    }

    /// <summary>
    /// The reservations of the recordings in flight when a start was refused for want of a tuner.
    /// </summary>
    public static IReadOnlyList<Guid> AtTheRefusal(IReadOnlyList<Recording> inFlight)
    {
        ArgumentNullException.ThrowIfNull(inFlight);

        return
        [
            .. inFlight
                .Where(recording => recording.ReservationId is not null)
                .Select(recording => recording.ReservationId!.Value),
        ];
    }
}
