using Carina.Contracts;
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
        var at = clock.GetUtcNow().UtcDateTime;
        var tally = new Tally();

        foreach (var change in difference.Services)
        {
            await ApplyOneAsync(change, covered, at, tally, cancellationToken);
        }

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

        var left = stored.Count - leaving.Length + arriving.Length;

        if (change.Kind is ScanChangeKind.Missing && left <= 0)
        {
            await services.RemoveAsync(change.NetworkId, change.ServiceId, cancellationToken);
            tally.ServicesRemoved++;

            return;
        }

        var known = await services.FindAsync(change.NetworkId, change.ServiceId, cancellationToken);

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
        else
        {
            known.Describe(change.Name, change.Category, at);
            await services.SaveAsync(known, cancellationToken);
            tally.ServicesUpdated++;
        }

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
                SelectionSource.Manual,
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
