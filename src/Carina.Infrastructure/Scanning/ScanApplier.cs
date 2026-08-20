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

        Tally tally = await writes.AllOrNothingAsync(
            async token =>
            {
                var counted = new Tally();
                DateTime at = clock.GetUtcNow().UtcDateTime;
                var named = new HashSet<(NetworkId, ServiceId)>();

                foreach (ScanServiceChange change in difference.Services)
                {
                    named.Add((change.NetworkId, change.ServiceId));

                    await ApplyOneAsync(change, covered, at, counted, token);
                }

                await ReconsiderTheRestAsync(named, covered, at, token);

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
        IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        ScanChannelChange[] arriving = change.Channels
            .Where(channel => channel.Kind is ScanChangeKind.Added)
            .Where(channel => covered.Contains(channel.Tuning.System))
            .ToArray();
        ScanChannelChange[] leaving = change.Channels
            .Where(channel => channel.Kind is ScanChangeKind.Missing)
            .Where(channel => covered.Contains(channel.Tuning.System))
            .ToArray();

        int moved = 0;

        foreach (ScanChannelChange? gone in leaving)
        {
            if (stored.FirstOrDefault(candidate => candidate.Tuning.Equals(gone.Tuning)) is { } dropped)
            {
                await candidates.RemoveAsync(dropped.Id, cancellationToken);
                tally.ChannelsRemoved++;
                moved++;
            }
        }

        IReadOnlyList<CandidateChannel> left = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        if (change.Kind is ScanChangeKind.Missing && arriving.Length == 0 && left.Count == 0)
        {
            if (await services.RemoveAsync(change.NetworkId, change.ServiceId, cancellationToken))
            {
                tally.ServicesRemoved++;
            }

            return;
        }

        BroadcastService? known = await services.FindAsync(change.NetworkId, change.ServiceId, cancellationToken);

        if (known is null && !change.Seen)
        {
            return;
        }

        if (known is null)
        {
            BroadcastService discovered = BroadcastService.Discover(
                change.NetworkId,
                change.ServiceId,
                change.Name,
                change.Category,
                at);

            discovered.RemoteControlledBy(change.RemoteControlKeyId);

            await services.AddAsync(discovered, cancellationToken);
            tally.ServicesAdded++;
        }
        else if (change.Seen)
        {
            known.Describe(change.Name, change.Category, at);
            known.RemoteControlledBy(change.RemoteControlKeyId);

            await services.SaveAsync(known, cancellationToken);
        }

        foreach (ScanChannelChange? arrival in arriving)
        {
            if (stored.FirstOrDefault(candidate => candidate.Tuning.Equals(arrival.Tuning)) is { } already)
            {
                if (arrival.TransportStreamId is { } seen && !seen.Equals(already.ObservedStreamId))
                {
                    already.CarriedBy(seen);

                    await candidates.SaveAsync(already, cancellationToken);
                }

                continue;
            }

            var candidate = CandidateChannel.Discover(
                CandidateChannelId.New(),
                change.NetworkId,
                change.ServiceId,
                arrival.Tuning,
                at);

            candidate.CarriedBy(arrival.TransportStreamId);

            if (arrival.Measurement is { } measurement)
            {
                candidate.RecordTuningSuccess(measurement, at);
            }

            await candidates.AddAsync(candidate, cancellationToken);
            tally.ChannelsAdded++;
            moved++;
        }

        if (known is not null && (change.Seen || moved > 0))
        {
            tally.ServicesUpdated++;
        }

        IReadOnlyList<CandidateChannel> settled = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        await LetTheScanDecideAsync(known, settled, at, cancellationToken);
    }

    private async Task ReconsiderTheRestAsync(
        HashSet<(NetworkId, ServiceId)> named,
        HashSet<TuneSystem> covered,
        DateTime at,
        CancellationToken cancellationToken)
    {
        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            if (named.Contains((service.NetworkId, service.ServiceId)))
            {
                continue;
            }

            IReadOnlyList<CandidateChannel> settled = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                cancellationToken);

            if (!settled.Any(candidate => covered.Contains(candidate.Tuning.System)))
            {
                continue;
            }

            await LetTheScanDecideAsync(service, settled, at, cancellationToken);
        }
    }

    private async Task LetTheScanDecideAsync(
        BroadcastService? known,
        IReadOnlyList<CandidateChannel> settled,
        DateTime at,
        CancellationToken cancellationToken)
    {
        if (!TheScanDecides(known, settled) || CandidateOrder.Best(settled) is not { } best)
        {
            return;
        }

        await candidates.SelectAsync(
            best.Id,
            SelectionSource.Scan,
            best.LastMeasurement,
            at,
            cancellationToken);
    }

    private static bool TheScanDecides(BroadcastService? known, IReadOnlyList<CandidateChannel> settled)
        => known is null
            || settled.FirstOrDefault(candidate => candidate.IsSelected)
                is { SelectionSource: SelectionSource.Scan, SelectionMeasurement: null };

    private sealed class Tally
    {
        public int ServicesAdded { get; set; }

        public int ServicesUpdated { get; set; }

        public int ServicesRemoved { get; set; }

        public int ChannelsAdded { get; set; }

        public int ChannelsRemoved { get; set; }
    }
}
