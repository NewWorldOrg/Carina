using Carina.Domain.Channels;

namespace Carina.Infrastructure.Collection;

public sealed class BroadcastStreamDirectory(ICandidateChannelRepository candidates)
    : IBroadcastStreamDirectory
{
    public async Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> selected = await candidates.ListSelectedAsync(cancellationToken);
        var streams = new List<BroadcastStream>();

        IEnumerable<IGrouping<(int Network, int Stream), CandidateChannel>> carried = selected
            .Where(candidate => candidate.ObservedStreamId is not null)
            .GroupBy(candidate => (candidate.NetworkId.Value, candidate.ObservedStreamId!.Value));

        foreach (IGrouping<(int Network, int Stream), CandidateChannel> group in carried)
        {
            if (group.FirstOrDefault(candidate => candidate.IsInRotation) is not { } reachable)
            {
                continue;
            }

            streams.Add(new BroadcastStream(
                reachable.NetworkId,
                reachable.ObservedStreamId!,
                reachable.Tuning,
                [.. group.Select(candidate => candidate.ServiceId).DistinctBy(service => service.Value).OrderBy(service => service.Value)]));
        }

        return [.. streams.OrderBy(stream => stream.NetworkId.Value).ThenBy(stream => stream.TransportStreamId.Value)];
    }
}
