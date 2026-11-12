using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Scans;

/// <summary>
/// Matches what a scan saw against the services and candidate channels held: it proposes the difference, and once
/// that is confirmed it writes it, touching only the systems the scan walked.
/// </summary>
public sealed class ChannelLineupDomainService(
    IBroadcastServiceRepository services,
    ICandidateChannelRepository candidates)
{
    /// <summary>
    /// The difference between the services held and those seen on the channels the scan reached. A held service seen
    /// nowhere loses the channels the scan reached, and is missing once it has no other; a service seen is changed
    /// when a channel is added, carries another stream or is gone, or when its name, kind or remote-control key moved;
    /// a service seen but not held is added.
    /// </summary>
    public async Task<ScanDifference> ProposeAsync(
        IReadOnlyList<ScannedService> seen,
        IReadOnlyCollection<TuningParameters> reached,
        IReadOnlyList<RotationDeparture> departures,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(reached);
        ArgumentNullException.ThrowIfNull(departures);

        Dictionary<(NetworkId, ServiceId), ScannedService> unheld = seen.ToDictionary(
            service => (service.NetworkId, service.ServiceId));
        HashSet<TuningParameters> walked = [.. reached];
        List<ScanServiceChange> changes = [];

        foreach (BroadcastService service in await services.ListAsync(cancellationToken))
        {
            IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
                service.NetworkId,
                service.ServiceId,
                cancellationToken);
            CandidateChannel[] inScope = [.. stored.Where(candidate => walked.Contains(candidate.Tuning))];

            ScanServiceChange? change = unheld.Remove((service.NetworkId, service.ServiceId), out ScannedService? scanned)
                ? Changed(service, stored, inScope, scanned)
                : Unseen(service, stored, inScope);

            if (change is not null)
            {
                changes.Add(change);
            }
        }

        changes.AddRange(seen
            .Where(service => unheld.ContainsKey((service.NetworkId, service.ServiceId)))
            .Select(service => new ScanServiceChange(
                ScanChangeKind.Added,
                service.NetworkId,
                service.ServiceId,
                service.Name,
                service.Category,
                service.Channels,
                Seen: true)
            {
                RemoteControlKeyId = service.RemoteControlKeyId,
            }));

        return new ScanDifference(changes, departures);
    }

    /// <summary>
    /// Writes a confirmed difference, only on the channels of the systems the scan walked: a channel gone is dropped,
    /// a service left with no channel is removed, a service not held is added only when the scan saw it, a channel
    /// already held takes the stream seen, and a channel found is added. Then each service whose channel was chosen
    /// blind by a scan, or that is new, is moved onto its best channel, and so is every other service with a channel
    /// on those systems.
    /// </summary>
    public async Task<ChannelLineupApplied> ApplyAsync(
        ScanDifference difference,
        IReadOnlySet<TuneSystem> covered,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(difference);
        ArgumentNullException.ThrowIfNull(covered);

        Tally tally = new();
        HashSet<(NetworkId, ServiceId)> named = [];

        foreach (ScanServiceChange change in difference.Services)
        {
            named.Add((change.NetworkId, change.ServiceId));

            await ApplyOneAsync(change, covered, at, tally, cancellationToken);
        }

        await ReconsiderTheRestAsync(named, covered, at, cancellationToken);

        return new ChannelLineupApplied(
            tally.ServicesAdded,
            tally.ServicesUpdated,
            tally.ServicesRemoved,
            tally.ChannelsAdded,
            tally.ChannelsRemoved);
    }

    private static ScanServiceChange? Unseen(
        BroadcastService service,
        IReadOnlyList<CandidateChannel> stored,
        CandidateChannel[] inScope)
    {
        if (inScope.Length == 0)
        {
            return null;
        }

        return new ScanServiceChange(
            inScope.Length == stored.Count ? ScanChangeKind.Missing : ScanChangeKind.Updated,
            service.NetworkId,
            service.ServiceId,
            service.Name,
            service.Category,
            MissingChannels(inScope),
            Seen: false);
    }

    private static ScanServiceChange? Changed(
        BroadcastService service,
        IReadOnlyList<CandidateChannel> stored,
        CandidateChannel[] inScope,
        ScannedService scanned)
    {
        ScanChannelChange[] added =
        [
            .. scanned.Channels.Where(channel => stored.All(candidate => !candidate.Tuning.Equals(channel.Tuning))),
        ];
        ScanChannelChange[] restated =
        [
            .. scanned.Channels.Where(channel => stored.Any(candidate =>
                candidate.Tuning.Equals(channel.Tuning) && candidate.IsRestatedBy(channel.TransportStreamId))),
        ];
        IReadOnlyList<ScanChannelChange> missing = MissingChannels(
            inScope.Where(candidate => scanned.Channels.All(channel => !channel.Tuning.Equals(candidate.Tuning))));
        bool described = service.Name != scanned.Name || service.Category != scanned.Category;
        bool renumbered = scanned.RemoteControlKeyId is not null
            && service.RemoteControlKeyId != scanned.RemoteControlKeyId;

        if (added.Length == 0 && restated.Length == 0 && missing.Count == 0 && !described && !renumbered)
        {
            return null;
        }

        return new ScanServiceChange(
            ScanChangeKind.Updated,
            service.NetworkId,
            service.ServiceId,
            scanned.Name,
            scanned.Category,
            [.. added, .. restated, .. missing],
            Seen: true)
        {
            RemoteControlKeyId = scanned.RemoteControlKeyId,
        };
    }

    private static IReadOnlyList<ScanChannelChange> MissingChannels(IEnumerable<CandidateChannel> stored)
        => [.. stored.Select(candidate => new ScanChannelChange(
            ScanChangeKind.Missing,
            candidate.Tuning,
            candidate.Tuning.TransportStreamId,
            candidate.LastMeasurement))];

    private async Task ApplyOneAsync(
        ScanServiceChange change,
        IReadOnlySet<TuneSystem> covered,
        DateTime at,
        Tally tally,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> stored = await candidates.ListForServiceAsync(
            change.NetworkId,
            change.ServiceId,
            cancellationToken);

        ScanChannelChange[] arriving = Walked(change, ScanChangeKind.Added, covered);
        ScanChannelChange[] leaving = Walked(change, ScanChangeKind.Missing, covered);

        int moved = await DropAsync(stored, leaving, tally, cancellationToken);

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

        await DescribeAsync(known, change, at, tally, cancellationToken);

        moved += await TakeAsync(stored, arriving, change, at, tally, cancellationToken);

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

    private static ScanChannelChange[] Walked(
        ScanServiceChange change,
        ScanChangeKind kind,
        IReadOnlySet<TuneSystem> covered)
        => [.. change.Channels.Where(channel => channel.Kind == kind && covered.Contains(channel.Tuning.System))];

    private async Task<int> DropAsync(
        IReadOnlyList<CandidateChannel> stored,
        ScanChannelChange[] leaving,
        Tally tally,
        CancellationToken cancellationToken)
    {
        int dropped = 0;

        foreach (ScanChannelChange gone in leaving)
        {
            if (stored.FirstOrDefault(candidate => candidate.Tuning.Equals(gone.Tuning)) is { } held)
            {
                await candidates.RemoveAsync(held.Id, cancellationToken);
                tally.ChannelsRemoved++;
                dropped++;
            }
        }

        return dropped;
    }

    private async Task DescribeAsync(
        BroadcastService? known,
        ScanServiceChange change,
        DateTime at,
        Tally tally,
        CancellationToken cancellationToken)
    {
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

            return;
        }

        if (change.Seen)
        {
            known.Describe(change.Name, change.Category, at);
            known.RemoteControlledBy(change.RemoteControlKeyId);

            await services.SaveAsync(known, cancellationToken);
        }
    }

    private async Task<int> TakeAsync(
        IReadOnlyList<CandidateChannel> stored,
        ScanChannelChange[] arriving,
        ScanServiceChange change,
        DateTime at,
        Tally tally,
        CancellationToken cancellationToken)
    {
        int added = 0;

        foreach (ScanChannelChange arrival in arriving)
        {
            if (stored.FirstOrDefault(candidate => candidate.Tuning.Equals(arrival.Tuning)) is { } already)
            {
                if (already.Restate(arrival.TransportStreamId))
                {
                    await candidates.SaveAsync(already, cancellationToken);
                }

                continue;
            }

            await candidates.AddAsync(
                CandidateChannel.FoundByScan(
                    CandidateChannelId.New(),
                    change.NetworkId,
                    change.ServiceId,
                    arrival.Tuning,
                    arrival.TransportStreamId,
                    arrival.Measurement,
                    at),
                cancellationToken);
            tally.ChannelsAdded++;
            added++;
        }

        return added;
    }

    private async Task ReconsiderTheRestAsync(
        HashSet<(NetworkId, ServiceId)> named,
        IReadOnlySet<TuneSystem> covered,
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
        => known is null || settled.FirstOrDefault(candidate => candidate.IsSelected) is { ChosenBlindByAScan: true };

    private sealed class Tally
    {
        public int ServicesAdded { get; set; }

        public int ServicesUpdated { get; set; }

        public int ServicesRemoved { get; set; }

        public int ChannelsAdded { get; set; }

        public int ChannelsRemoved { get; set; }
    }
}
