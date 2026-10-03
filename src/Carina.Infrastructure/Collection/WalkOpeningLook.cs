using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Collection;

/// <summary>
/// Looks for an opening to walk ahead of the sweep's time: reads the ledger for the streams whose visit is
/// due, and asks the driver for its tuners only when there is one.
/// </summary>
public sealed class WalkOpeningLook(
    IBroadcastStreamDirectory directory,
    IStreamVisitRepository visits,
    IDriverClient driver,
    CollectionSettings settings,
    TimeProvider clock)
{
    public async Task<bool> IsThereAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TuneSystem> awaited = WalkOpening.Awaited(
            await directory.ListAsync(cancellationToken),
            await visits.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime,
            settings);

        if (awaited.Count == 0)
        {
            return false;
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> asked = await driver.GetTunersAsync(cancellationToken);

        return asked.TryGetValue(out IReadOnlyList<TunerSnapshot>? tuners) && WalkOpening.IsThere(awaited, tuners);
    }
}
