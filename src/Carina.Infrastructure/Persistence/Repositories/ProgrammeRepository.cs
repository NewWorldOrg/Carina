using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    public async Task<IReadOnlyList<Programme>> ListEndedBeforeAsync(
        DateTime at,
        int rows,
        CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.EndsAt != null && programme.EndsAt < at)
            .OrderBy(programme => programme.EndsAt)
            .Take(rows)
            .ToListAsync(cancellationToken);

    public async Task<int> ForgetEndedBeforeAsync(DateTime at, CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.EndsAt == null ? programme.StartsAt < at : programme.EndsAt < at)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<Programme>> ListForServicesAsync(
        IReadOnlyList<ProgrammeService> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            return [];
        }

        List<NetworkId> networks = [.. services.Select(service => new NetworkId(service.NetworkId)).Distinct()];
        List<ServiceId> carried = [.. services.Select(service => new ServiceId(service.ServiceId)).Distinct()];
        List<Programme> found = await context.Set<Programme>()
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => programme.StartsAt < to)
            .Where(programme => programme.EndsAt == null || programme.EndsAt > from)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
        var wanted = services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return [.. found.Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))];
    }

    public async Task<PaginatedList<Programme>> SearchAsync(
        ProgrammeSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        string looking = $"%{search.Keyword.ToLowerInvariant()}%";
        IQueryable<Programme> found = context.Set<Programme>()
            .Where(programme => EF.Functions.Like(
                EF.Property<string>(programme, ProgrammeConfiguration.Searchable),
                looking));

        if (search.From is { } from)
        {
            found = found.Where(programme => programme.EndsAt == null || programme.EndsAt > from);
        }

        if (search.To is { } to)
        {
            found = found.Where(programme => programme.StartsAt < to);
        }

        int total = await found.CountAsync(cancellationToken);
        IOrderedQueryable<Programme> ordered = (search.Sort, search.Descending) switch
        {
            (ProgrammeSort.Name, false) => found.OrderBy(programme => programme.Name),
            (ProgrammeSort.Name, true) => found.OrderByDescending(programme => programme.Name),
            (_, true) => found.OrderByDescending(programme => programme.StartsAt),
            _ => found.OrderBy(programme => programme.StartsAt),
        };
        List<Programme> page = await ordered
            .ThenBy(programme => programme.EventId)
            .Skip((search.Page - 1) * search.PerPage)
            .Take(search.PerPage)
            .ToListAsync(cancellationToken);

        return new PaginatedList<Programme>(page, total, search.Page, search.PerPage);
    }

    public async Task<IReadOnlyList<Programme>> ListAfterAsync(
        long revision,
        int rows,
        CancellationToken cancellationToken)
        => await context.Set<Programme>()
            .Where(programme => programme.Revision > revision)
            .OrderBy(programme => programme.Revision)
            .Take(rows)
            .ToListAsync(cancellationToken);

    public async Task<long> NextRevisionAsync(CancellationToken cancellationToken)
    {
        await using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();

        command.CommandText = $"SELECT nextval('{ProgrammeRevisions.Sequence}')";
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();

        if (command.Connection!.State is not System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

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
