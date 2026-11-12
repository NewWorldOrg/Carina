using Puppis.Contracts;

namespace Carina.Domain.Channels;

/// <summary>
/// Decides which streams the guide is collected from and how each is tuned. Only selected candidates count. A stream
/// is visited when one of its candidates is in the rotation, and it is tuned through the best of those. The candidate
/// that speaks for a stream is the one in the rotation, then one backing off, then any other, with fewer failures
/// first.
/// </summary>
public sealed class BroadcastStreamDomainService(ICandidateChannelRepository candidates)
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
            if (CandidateOrder.Best(group.Where(candidate => candidate.IsInRotation)) is not { } reachable)
            {
                continue;
            }

            streams.Add(new BroadcastStream(
                reachable.NetworkId,
                reachable.ObservedStreamId!,
                reachable.Tuning,
                [.. group.Select(candidate => candidate.ServiceId).DistinctBy(service => service.Value).OrderBy(service => service.Value)])
            {
                TunedWith = reachable.Id,
            });
        }

        return [.. streams.OrderBy(stream => stream.NetworkId.Value).ThenBy(stream => stream.TransportStreamId.Value)];
    }

    public async Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CandidateChannel> selected = await candidates.ListSelectedAsync(cancellationToken);
        var intended = new List<IntendedStream>();

        foreach (IGrouping<StreamIdentity, CandidateChannel> group in selected.GroupBy(IdentityOf))
        {
            CandidateChannel leading = Leading(group);

            intended.Add(new IntendedStream(
                leading.NetworkId,
                leading.ObservedStreamId,
                leading.Tuning,
                [.. group.Select(candidate => candidate.ServiceId).DistinctBy(service => service.Value).OrderBy(service => service.Value)],
                new StreamReach(
                    leading.RotationState,
                    leading.ConsecutiveFailures,
                    leading.NextAttemptAt,
                    leading.NeedsAttentionSince)));
        }

        return
        [
            .. intended
                .OrderBy(stream => stream.NetworkId.Value)
                .ThenBy(stream => stream.TransportStreamId is null)
                .ThenBy(stream => stream.TransportStreamId?.Value ?? 0)
                .ThenBy(stream => stream.Tuning.PhysicalChannel),
        ];
    }

    private static StreamIdentity IdentityOf(CandidateChannel candidate)
        => candidate.ObservedStreamId is { } observed
            ? new StreamIdentity(candidate.NetworkId.Value, observed.Value, null, null, null)
            : new StreamIdentity(
                candidate.NetworkId.Value,
                null,
                candidate.Tuning.System,
                candidate.Tuning.PhysicalChannel,
                candidate.Tuning.TransportStreamId?.Value);

    private static CandidateChannel Leading(IEnumerable<CandidateChannel> group)
        => group
            .OrderBy(candidate => candidate.RotationState switch
            {
                RotationState.Active => 0,
                RotationState.BackingOff => 1,
                _ => 2,
            })
            .ThenBy(candidate => candidate.ConsecutiveFailures)
            .ThenBy(candidate => candidate.NextAttemptAt ?? DateTime.MinValue)
            .ThenBy(candidate => candidate, CandidateOrder.ByWhatWasMeasured)
            .First();

    private readonly record struct StreamIdentity(
        int Network,
        int? Observed,
        TuneSystem? System,
        int? PhysicalChannel,
        int? Carried);
}
