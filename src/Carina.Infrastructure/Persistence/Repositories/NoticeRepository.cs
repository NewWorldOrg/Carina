using Carina.Domain.Notifications;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class NoticeRepository(CarinaDbContext context) : INoticeRepository
{
    public async Task<Notice?> FindAsync(NoticeId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await context.Set<Notice>()
            .SingleOrDefaultAsync(notice => notice.Id == id, cancellationToken);
    }

    public async Task<IReadOnlySet<NoticeKey>> HeldAmongAsync(
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);

        if (sourceIds.Count is 0)
        {
            return new HashSet<NoticeKey>();
        }

        Guid[] asked = [.. sourceIds.Distinct()];

        List<NoticeKey> held = await context.Set<Notice>()
            .AsNoTracking()
            .Where(notice => asked.Contains(notice.SourceId))
            .Select(notice => new NoticeKey(notice.Kind, notice.SourceId))
            .ToListAsync(cancellationToken);

        return held.ToHashSet();
    }

    /// <summary>
    /// Writes the notice and its deliveries in one transaction, or none of them. Nothing stays tracked
    /// afterwards, so what was refused is not written by a later save.
    /// </summary>
    public async Task<bool> AddAsync(
        Notice notice,
        IReadOnlyList<NotificationDelivery> deliveries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(deliveries);

        context.Add(notice);
        context.AddRange(deliveries);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException refusal) when (IsAlreadyHeld(refusal))
        {
            return false;
        }
        finally
        {
            context.Entry(notice).State = EntityState.Detached;

            foreach (NotificationDelivery delivery in deliveries)
            {
                context.Entry(delivery).State = EntityState.Detached;
            }
        }
    }

    private static bool IsAlreadyHeld(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NoticeConfiguration.SourceIndexName,
        };
}
