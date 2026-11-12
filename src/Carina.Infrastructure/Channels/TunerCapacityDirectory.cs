using Carina.Domain.Channels;
using Carina.Domain.Driver;

using Puppis.Contracts;

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

        return TunerCapacity.Of(
            document.Tuners,
            tuners.TryGetValue(out IReadOnlyList<TunerSnapshot>? snapshots) ? snapshots : []);
    }
}
