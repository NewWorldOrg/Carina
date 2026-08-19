using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class StreamVisitRepository(CarinaDbContext context) : IStreamVisitRepository
{
    public async Task<StreamVisit?> FindAsync(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);

        return await context.Set<StreamVisit>()
            .FirstOrDefaultAsync(
                visit => visit.NetworkId == networkId && visit.TransportStreamId == transportStreamId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StreamVisit>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<StreamVisit>()
            .OrderBy(visit => visit.NetworkId)
            .ThenBy(visit => visit.TransportStreamId)
            .ToListAsync(cancellationToken);

    public async Task SaveAsync(StreamVisit visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        StreamVisit? held = await FindAsync(visit.NetworkId, visit.TransportStreamId, cancellationToken);

        if (held is null)
        {
            await context.AddAsync(visit, cancellationToken);
        }
        else if (!ReferenceEquals(held, visit))
        {
            context.Entry(held).CurrentValues.SetValues(visit);
            held.Tallied(visit.Tally);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
