using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public static class RecordedInstead
{
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
