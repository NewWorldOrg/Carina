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

                foreach (ScanServiceChange change in difference.Services)
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
        }

        foreach (ScanChannelChange? arrival in arriving)
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
            moved++;
        }

        if (known is not null)
        {
            if (change.Seen || moved > 0)
            {
                tally.ServicesUpdated++;
            }

            return;
        }

        IReadOnlyList<CandidateChannel> settled = await candidates.ListForServiceAsync(
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
