using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public interface IStreamVisitRepository
{
    Task<StreamVisit?> FindAsync(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<StreamVisit>> ListAsync(CancellationToken cancellationToken);

    Task SaveAsync(StreamVisit visit, CancellationToken cancellationToken);
}
