using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ProgrammeRepository(CarinaDbContext context) : IProgrammeRepository
{
    public async Task<Programme?> FindAsync(ProgrammeId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Programme>()
            .FirstOrDefaultAsync(
                programme => programme.NetworkId == id.NetworkId
                    && programme.ServiceId == id.ServiceId
                    && programme.EventId == id.EventId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Programme>> ListAsync(
        ProgrammeWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        var networkId = new NetworkId(window.NetworkId);
        var serviceId = new ServiceId(window.ServiceId);

        return await context.Set<Programme>()
            .Where(programme => programme.NetworkId == networkId && programme.ServiceId == serviceId)
            .Where(programme => programme.StartsAt < window.To)
            .Where(programme => programme.EndsAt == null || programme.EndsAt > window.From)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Programme programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        await context.AddAsync(programme, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(Programme programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        context.Update(programme);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ForgetEndedBeforeAsync(DateTime at, CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.EndsAt == null ? programme.StartsAt < at : programme.EndsAt < at)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<int> ForgetEverythingAsync(CancellationToken cancellationToken)
        => await context.Set<Programme>().ExecuteDeleteAsync(cancellationToken);

    public async Task<DateTime?> CoveredUntilAsync(
        int networkId,
        int serviceId,
        CancellationToken cancellationToken)
    {
        var network = new NetworkId(networkId);
        var service = new ServiceId(serviceId);

        return await context.Set<Programme>()
            .Where(programme => programme.NetworkId == network && programme.ServiceId == service)
            .Where(programme => !programme.IsShadow)
            .MaxAsync(programme => (DateTime?)programme.StartsAt, cancellationToken);
    }
}
