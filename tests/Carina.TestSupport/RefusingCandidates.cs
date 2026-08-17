using Carina.Domain.Channels;

namespace Carina.TestSupport;

/// <summary>
/// The store it wraps until the gate says so, and a refusal from then on. Stands in for
/// whatever ends an apply half way — a dropped connection, a constraint, a cancelled request.
/// </summary>
public sealed class RefusingCandidates(ICandidateChannelRepository candidates, Func<bool> refuses)
    : ICandidateChannelRepository
{

    public Task<CandidateChannel?> FindAsync(CandidateChannelId id, CancellationToken cancellationToken)
        => candidates.FindAsync(id, cancellationToken);

    public Task<IReadOnlyList<CandidateChannel>> ListForServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => candidates.ListForServiceAsync(networkId, serviceId, cancellationToken);

    public Task<CandidateChannel?> FindSelectedAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => candidates.FindSelectedAsync(networkId, serviceId, cancellationToken);

    public Task<IReadOnlyList<CandidateChannel>> ListInRotationAsync(
        DateTime at,
        CancellationToken cancellationToken)
        => candidates.ListInRotationAsync(at, cancellationToken);

    public Task<IReadOnlyList<CandidateChannel>> ListNeedingAttentionAsync(CancellationToken cancellationToken)
        => candidates.ListNeedingAttentionAsync(cancellationToken);

    public Task AddAsync(CandidateChannel candidate, CancellationToken cancellationToken)
        => refuses()
            ? throw new InvalidOperationException("This store stopped taking candidates part way through.")
            : candidates.AddAsync(candidate, cancellationToken);

    public Task SaveAsync(CandidateChannel candidate, CancellationToken cancellationToken)
        => candidates.SaveAsync(candidate, cancellationToken);

    public Task<CandidateChannel?> SelectAsync(
        CandidateChannelId id,
        SelectionSource source,
        SignalMeasurement? measuredAtSelection,
        DateTime at,
        CancellationToken cancellationToken)
        => candidates.SelectAsync(id, source, measuredAtSelection, at, cancellationToken);

    public Task ClearSelectionAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => candidates.ClearSelectionAsync(networkId, serviceId, cancellationToken);

    public Task RequireRevalidationAsync(CancellationToken cancellationToken)
        => candidates.RequireRevalidationAsync(cancellationToken);

    public Task RemoveAsync(CandidateChannelId id, CancellationToken cancellationToken)
        => candidates.RemoveAsync(id, cancellationToken);
}
