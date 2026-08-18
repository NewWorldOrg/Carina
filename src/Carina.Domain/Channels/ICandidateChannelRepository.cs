namespace Carina.Domain.Channels;

public interface ICandidateChannelRepository
{
    Task<CandidateChannel?> FindAsync(CandidateChannelId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CandidateChannel>> ListForServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task<CandidateChannel?> FindSelectedAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CandidateChannel>> ListInRotationAsync(DateTime at, CancellationToken cancellationToken);

    Task<IReadOnlyList<CandidateChannel>> ListSelectedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<CandidateChannel>> ListNeedingAttentionAsync(CancellationToken cancellationToken);

    Task AddAsync(CandidateChannel candidate, CancellationToken cancellationToken);

    Task SaveAsync(CandidateChannel candidate, CancellationToken cancellationToken);

    Task<CandidateChannel?> SelectAsync(
        CandidateChannelId id,
        SelectionSource source,
        SignalMeasurement? measuredAtSelection,
        DateTime at,
        CancellationToken cancellationToken);

    Task ClearSelectionAsync(NetworkId networkId, ServiceId serviceId, CancellationToken cancellationToken);

    Task RequireRevalidationAsync(CancellationToken cancellationToken);

    Task RemoveAsync(CandidateChannelId id, CancellationToken cancellationToken);
}
