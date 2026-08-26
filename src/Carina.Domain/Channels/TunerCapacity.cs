using Carina.Contracts;

namespace Carina.Domain.Channels;

public sealed record TunerSeat(string DeviceId, IReadOnlyList<TuneSystem> Serves, bool Faulted);

public sealed class TunerCapacity
{
    private readonly IReadOnlyList<TunerSeat> seats;

    public TunerCapacity(IReadOnlyList<TunerSeat> seats, IReadOnlyList<string> undetermined)
    {
        ArgumentNullException.ThrowIfNull(seats);
        ArgumentNullException.ThrowIfNull(undetermined);

        this.seats = seats;
        Undetermined = undetermined;
    }

    public IReadOnlyList<string> Undetermined { get; }

    public int SeatCount => seats.Count;

    public IReadOnlySet<TuneSystem> Reachable =>
        seats.SelectMany(seat => seat.Serves).ToHashSet();

    public TunerCapacity Healthy =>
        new([.. seats.Where(seat => !seat.Faulted)], Undetermined);

    public bool CanServe(TuneSystem system) =>
        seats.Any(seat => seat.Serves.Contains(system));

    public bool CanSeat(IReadOnlyDictionary<TuneSystem, int> demand)
    {
        ArgumentNullException.ThrowIfNull(demand);

        if (demand.Any(want => want.Value < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(demand),
                "A demand counts the tuners a broadcast type needs at once, and no type can need fewer than none.");
        }

        TuneSystem[] wanted = [.. demand.Where(want => want.Value > 0).Select(want => want.Key).Distinct()];

        for (int subset = 1; subset < 1 << wanted.Length; subset++)
        {
            TuneSystem[] together = [.. wanted.Where((_, index) => (subset & (1 << index)) != 0)];
            int needed = together.Sum(system => demand[system]);
            int able = seats.Count(seat => together.Any(system => seat.Serves.Contains(system)));

            if (needed > able)
            {
                return false;
            }
        }

        return true;
    }
}

public static class BroadcastReception
{
    public static IReadOnlyList<TuneSystem> EverySystem { get; } =
        [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110];

    public static IReadOnlyList<TuneSystem> Of(TunerKind kind) => kind switch
    {
        TunerKind.Terrestrial => [TuneSystem.IsdbT],
        TunerKind.Satellite => [TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
        _ => [],
    };
}
