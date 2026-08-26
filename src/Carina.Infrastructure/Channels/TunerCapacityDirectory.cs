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

        HashSet<string> faulted = tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? snapshots)
            ? [.. snapshots.Where(snapshot => snapshot.State is TunerState.Faulted).Select(snapshot => snapshot.DeviceId)]
            : new HashSet<string>(StringComparer.Ordinal);

        var seats = new List<TunerSeat>();
        var undetermined = new List<string>();

        foreach (TunerConfigEntry entry in document.Tuners.Where(entry => !entry.Disabled))
        {
            if (BroadcastReception.Of(entry.Kind) is not { Count: > 0 } serves)
            {
                undetermined.Add(entry.DeviceId);

                continue;
            }

            seats.Add(new TunerSeat(entry.DeviceId, serves, faulted.Contains(entry.DeviceId)));
        }

        return new TunerCapacity(
            [.. seats.OrderBy(seat => seat.DeviceId, StringComparer.Ordinal)],
            [.. undetermined.Order(StringComparer.Ordinal)]);
    }
}
