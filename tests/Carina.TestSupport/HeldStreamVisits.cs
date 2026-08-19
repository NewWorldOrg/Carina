using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.TestSupport;

public sealed class HeldStreamVisits : IStreamVisitRepository
{
    public List<StreamVisit> Visits { get; } = [];

    public Task<StreamVisit?> FindAsync(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        CancellationToken cancellationToken)
        => Task.FromResult(Visits.FirstOrDefault(visit =>
            visit.NetworkId.Equals(networkId) && visit.TransportStreamId.Equals(transportStreamId)));

    public Task<IReadOnlyList<StreamVisit>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StreamVisit>>([.. Visits]);

    public Task SaveAsync(StreamVisit visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        Visits.RemoveAll(held =>
            held.NetworkId.Equals(visit.NetworkId)
            && held.TransportStreamId.Equals(visit.TransportStreamId));
        Visits.Add(visit);

        return Task.CompletedTask;
    }
}

public sealed class HeldStreams(IReadOnlyList<BroadcastStream> streams) : IBroadcastStreamDirectory
{
    public List<IntendedStream> Unreachable { get; } = [];

    public Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(streams);

    public Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IntendedStream>>(
        [
            .. streams.Select(stream => new IntendedStream(
                stream.NetworkId,
                stream.TransportStreamId,
                stream.Tuning,
                stream.Services,
                StreamReach.Reachable)),
            .. Unreachable,
        ]);
}
