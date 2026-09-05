using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LogoVisitRepository(CarinaDbContext context) : ILogoVisitRepository
{
    public async Task<IReadOnlyList<LogoVisit>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<LogoVisit>()
            .OrderBy(visit => visit.NetworkId)
            .ThenBy(visit => visit.TransportStreamId)
            .ToListAsync(cancellationToken);

    public async Task RecordAsync(
        NetworkId networkId,
        TransportStreamId transportStreamId,
        LogoVisitOutcome outcome,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(transportStreamId);

        LogoVisit? held = await context.Set<LogoVisit>()
            .FirstOrDefaultAsync(
                visit => visit.NetworkId == networkId && visit.TransportStreamId == transportStreamId,
                cancellationToken);

        if (held is null)
        {
            context.Add(LogoVisit.Record(networkId, transportStreamId, outcome, at));
        }
        else
        {
            held.Record(outcome, at);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
