using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Scans;

using Puppis.Contracts;

namespace Carina.Infrastructure.Scanning;

public sealed record ScanApplication(
    IReadOnlyList<TuneSystem> Systems,
    int ServicesAdded,
    int ServicesUpdated,
    int ServicesRemoved,
    int ChannelsAdded,
    int ChannelsRemoved);

public sealed class ScanApplier(
    ChannelLineupDomainService lineup,
    IAtomicWrite writes,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ScanApplication> ApplyAsync(
        ScanDifference difference,
        IReadOnlyList<TuneSystem> systems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(systems);

        HashSet<TuneSystem> covered = systems.ToHashSet();

        ChannelLineupApplied applied = await writes.AllOrNothingAsync(
            token => lineup.ApplyAsync(difference, covered, clock.GetUtcNow().UtcDateTime, token),
            cancellationToken);

        events.Signal(AppEventName.Tuners);

        return new ScanApplication(
            [.. covered],
            applied.ServicesAdded,
            applied.ServicesUpdated,
            applied.ServicesRemoved,
            applied.ChannelsAdded,
            applied.ChannelsRemoved);
    }
}
