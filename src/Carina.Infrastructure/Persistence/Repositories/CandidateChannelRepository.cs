using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class CandidateChannelRepository(CarinaDbContext context) : ICandidateChannelRepository
{
    private static readonly string[] RotationProperties =
    [
        nameof(CandidateChannel.NeedsRevalidation),
        nameof(CandidateChannel.RotationState),
        nameof(CandidateChannel.ConsecutiveFailures),
        nameof(CandidateChannel.NextAttemptAt),
        nameof(CandidateChannel.NeedsAttentionSince),
        nameof(CandidateChannel.LastSeenAt),
    ];

    public async Task<CandidateChannel?> FindAsync(CandidateChannelId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await Candidates().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CandidateChannel>> ListForServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return await OfService(networkId, serviceId)
            .OrderByDescending(candidate => candidate.IsSelected)
            .ThenBy(candidate => candidate.DiscoveredAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CandidateChannel?> FindSelectedAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return await OfService(networkId, serviceId)
            .FirstOrDefaultAsync(candidate => candidate.IsSelected, cancellationToken);
    }

    public async Task<IReadOnlyList<CandidateChannel>> ListInRotationAsync(
        DateTime at,
        CancellationToken cancellationToken)
        => await Candidates()
            .Where(candidate => candidate.RotationState != RotationState.NeedsAttention)
            .Where(candidate => candidate.NextAttemptAt == null || candidate.NextAttemptAt <= at)
            .OrderBy(candidate => candidate.NetworkId)
            .ThenBy(candidate => candidate.ServiceId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CandidateChannel>> ListNeedingAttentionAsync(
        CancellationToken cancellationToken)
        => await Candidates()
            .Where(candidate => candidate.RotationState == RotationState.NeedsAttention)
            .OrderBy(candidate => candidate.NeedsAttentionSince)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(CandidateChannel candidate, CancellationToken cancellationToken)
    {
        context.Add(candidate);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(CandidateChannel candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var entry = context.Entry(candidate);

        if (entry.State is EntityState.Detached)
        {
            entry = context.Attach(candidate);

            foreach (var property in RotationProperties)
            {
                entry.Property(property).IsModified = true;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CandidateChannel?> SelectAsync(
        CandidateChannelId id,
        SelectionSource source,
        SignalMeasurement? measuredAtSelection,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        var chosen = await FindAsync(id, cancellationToken);
        if (chosen is null)
        {
            return null;
        }

        // A selection is atomic on its own, but as one step of a larger write it joins that
        // write instead of committing the deselect-then-select pair ahead of it.
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await DeselectAsync(chosen.NetworkId, chosen.ServiceId, cancellationToken);

        chosen.Select(source, measuredAtSelection, at);
        await context.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(CancellationToken.None);
        }

        return chosen;
    }

    public async Task ClearSelectionAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        await DeselectAsync(networkId, serviceId, cancellationToken);
    }

    public async Task RequireRevalidationAsync(CancellationToken cancellationToken)
        => await context.Set<CandidateChannel>()
            .ExecuteUpdateAsync(
                update => update.SetProperty(candidate => candidate.NeedsRevalidation, true),
                cancellationToken);

    public async Task RemoveAsync(CandidateChannelId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        await context.Set<CandidateChannel>()
            .Where(candidate => candidate.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task DeselectAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        var selected = await OfService(networkId, serviceId)
            .Where(candidate => candidate.IsSelected)
            .ToListAsync(cancellationToken);

        if (selected.Count == 0)
        {
            return;
        }

        foreach (var candidate in selected)
        {
            candidate.Deselect();
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<CandidateChannel> OfService(NetworkId networkId, ServiceId serviceId)
        => Candidates()
            .Where(candidate => candidate.NetworkId == networkId && candidate.ServiceId == serviceId);

    private IQueryable<CandidateChannel> Candidates() => context.Set<CandidateChannel>();
}
