using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Channels;

public sealed class TunerCapacityDirectory(IDriverClient driver) : ITunerCapacityDirectory
{
    public async Task<TunerCapacity?> ReadAsync(CancellationToken cancellationToken)
    {
        DriverCall<TunerLedgerDto> ledger = await driver.GetTunerLedgerAsync(cancellationToken);

        if (!ledger.TryGetValue(out TunerLedgerDto? document))
        {
            return null;
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> tuners = await driver.GetTunersAsync(cancellationToken);

        if (!tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? snapshots))
        {
            return null;
        }

        var observed = snapshots
            .GroupBy(snapshot => snapshot.DeviceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var seats = new List<TunerSeat>();
        var undetermined = new List<string>();

        foreach (TunerConfigEntry entry in document.Tuners.Where(entry => !entry.Disabled))
        {
            if (!observed.TryGetValue(entry.DeviceId, out TunerSnapshot? snapshot)
                || BroadcastReception.Of(snapshot.Kind) is not { Count: > 0 } serves)
            {
                undetermined.Add(entry.DeviceId);

                continue;
            }

            seats.Add(new TunerSeat(entry.DeviceId, serves, snapshot.State is TunerState.Faulted));
        }

        return new TunerCapacity(
            [.. seats.OrderBy(seat => seat.DeviceId, StringComparer.Ordinal)],
            [.. undetermined.Order(StringComparer.Ordinal)]);
    }
}
