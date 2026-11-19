using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;

using Puppis.Contracts;

namespace Carina.Infrastructure.Collection;

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
