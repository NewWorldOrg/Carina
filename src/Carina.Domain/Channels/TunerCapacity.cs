using Carina.Contracts;

namespace Carina.Domain.Channels;

public sealed record TunerSeat(string DeviceId, IReadOnlyList<TuneSystem> Serves, bool Faulted);

public sealed record TunerCapacity(IReadOnlyList<TunerSeat> Seats, IReadOnlyList<string> Undetermined)
{
    public bool CanServe(TuneSystem system) =>
        Seats.Any(seat => seat.Serves.Contains(system));

    public IReadOnlyList<TuneSystem> Served =>
        [.. Seats.SelectMany(seat => seat.Serves).Distinct().Order()];
}

public static class BroadcastReception
{
    public static IReadOnlyList<TuneSystem> Of(TunerKind kind) => kind switch
    {
        TunerKind.Terrestrial => [TuneSystem.IsdbT],
        TunerKind.Satellite => [TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
        _ => [],
    };
}
