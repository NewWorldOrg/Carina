using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Scans;

namespace Carina.Infrastructure.Scanning;

public sealed record ScanApplication(
    IReadOnlyList<TuneSystem> Systems,
    int ServicesAdded,
    int ServicesUpdated,
    int ServicesRemoved,
    int ChannelsAdded,
    int ChannelsRemoved);

public sealed class ScanApplier(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates,
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

        var covered = systems.ToHashSet();

        // Half a difference is not a smaller difference. A service left without the candidate
        // channel that was to arrive with it cannot be told apart from one deliberately left
        // with no way to tune it, and a service never reached cannot be told apart from one the
        // scan did not find. A caller that goes away mid-apply therefore leaves nothing rather
        // than a prefix, and the difference it was applying stays applicable.
        //
        // What the write counted, and the moment it says the services were seen, are taken from
        // the write rather than from around it: a write that ran twice would otherwise report
        // the first run's clock and both runs' counts.
        var tally = await writes.AllOrNothingAsync(
            async token =>
            {
                var counted = new Tally();
                var at = clock.GetUtcNow().UtcDateTime;

                foreach (var change in difference.Services)
                {
                    await ApplyOneAsync(change, covered, at, counted, token);
                }

                return counted;
            },
            cancellationToken);

        events.Signal(AppEventName.Tuners);

        return new ScanApplication(
            [.. covered],
            tally.ServicesAdded,
            tally.ServicesUpdated,
            tally.ServicesRemoved,
            tally.ChannelsAdded,
            tally.ChannelsRemoved);
    }

    private async Task ApplyOneAsync(
        ScanServiceChange change,
        HashSet<TuneSystem> covered,
        DateTime at,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var stored = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        var arriving = change.Channels
            .Where(channel => channel.Kind is ScanChangeKind.Added)
            .Where(channel => covered.Contains(channel.Tuning.System))
            .ToArray();
        var leaving = change.Channels
            .Where(channel => channel.Kind is ScanChangeKind.Missing)
            .Where(channel => covered.Contains(channel.Tuning.System))
            .ToArray();

        foreach (var gone in leaving)
        {
            if (stored.FirstOrDefault(candidate => candidate.Tuning.Equals(gone.Tuning)) is { } dropped)
            {
                await candidates.RemoveAsync(dropped.Id, cancellationToken);
                tally.ChannelsRemoved++;
            }
        }

        var left = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        if (change.Kind is ScanChangeKind.Missing && arriving.Length == 0 && left.Count == 0)
        {
            await services.RemoveAsync(change.NetworkId, change.ServiceId, cancellationToken);
            tally.ServicesRemoved++;

            return;
        }

        var known = await services.FindAsync(change.NetworkId, change.ServiceId, cancellationToken);

        if (known is null && !change.Seen)
        {
            // Nothing received it and nothing holds it. Discovering it here would enter it as
            // seen just now, which is the stamp this change exists to withhold.
            return;
        }

        if (known is null)
        {
            await services.AddAsync(
                BroadcastService.Discover(
                    change.NetworkId,
                    change.ServiceId,
                    change.Name,
                    change.Category,
                    at),
                cancellationToken);
            tally.ServicesAdded++;
        }
        else if (change.Seen)
        {
            known.Describe(change.Name, change.Category, at);
            await services.SaveAsync(known, cancellationToken);
            tally.ServicesUpdated++;
        }
        // Anything else here did not receive the service, so its channels are what changed and
        // the service row is left alone: describing it would make the last-seen clock say the
        // service was received, and counting it would report an update nothing made.

        foreach (var arrival in arriving)
        {
            if (stored.Any(candidate => candidate.Tuning.Equals(arrival.Tuning)))
            {
                continue;
            }

            var candidate = CandidateChannel.Discover(
                CandidateChannelId.New(),
                change.NetworkId,
                change.ServiceId,
                arrival.Tuning,
                at);

            if (arrival.Measurement is { } measurement)
            {
                candidate.RecordTuningSuccess(measurement, at);
            }

            await candidates.AddAsync(candidate, cancellationToken);
            tally.ChannelsAdded++;
        }

        if (known is not null)
        {
            return;
        }

        var settled = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        if (Best(settled) is { } best)
        {
            await candidates.SelectAsync(
                best.Id,
                SelectionSource.Scan,
                best.LastMeasurement,
                at,
                cancellationToken);
        }
    }

    private static CandidateChannel? Best(IReadOnlyList<CandidateChannel> settled)
        => settled
            .OrderByDescending(candidate => candidate.LastMeasurement?.Locked ?? false)
            .ThenByDescending(candidate => candidate.LastMeasurement?.CnrMilliDecibels ?? int.MinValue)
            .ThenBy(candidate => candidate.Tuning.PhysicalChannel)
            .FirstOrDefault();

    private sealed class Tally
    {
        public int ServicesAdded { get; set; }

        public int ServicesUpdated { get; set; }

        public int ServicesRemoved { get; set; }

        public int ChannelsAdded { get; set; }

        public int ChannelsRemoved { get; set; }
    }
}
