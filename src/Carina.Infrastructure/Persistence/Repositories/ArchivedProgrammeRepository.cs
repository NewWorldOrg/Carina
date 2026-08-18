using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class ArchivedProgrammeRepository(CarinaDbContext context) : IArchivedProgrammeRepository
{
    public async Task<IReadOnlyList<ArchivedProgramme>> ListAsync(
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
        List<ArchivedProgramme> found = await context.Set<ArchivedProgramme>()
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => programme.StartsAt < to && programme.EndsAt > from)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
        var wanted = services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

        return [.. found.Where(programme => wanted.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))];
    }

    public async Task<int> KeepAsync(
        IReadOnlyList<ArchivedProgramme> programmes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        int kept = 0;

        foreach (ArchivedProgramme arriving in programmes)
        {
            ArchivedProgramme? held = await context.Set<ArchivedProgramme>()
                .FirstOrDefaultAsync(
                    programme => programme.NetworkId == arriving.NetworkId
                        && programme.ServiceId == arriving.ServiceId
                        && programme.EventId == arriving.EventId
                        && programme.StartsAt == arriving.StartsAt,
                    cancellationToken);

            if (held is null)
            {
                await context.Set<ArchivedProgramme>().AddAsync(arriving, cancellationToken);
                kept++;

                continue;
            }

            if (held.AbsorbTheRicherOf(arriving))
            {
                kept++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return kept;
    }

    public async Task<int> ForgetBeforeAsync(DateTime at, CancellationToken cancellationToken)
        => await context.Set<ArchivedProgramme>()
            .Where(programme => programme.EndsAt < at)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<int> ForgetServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
        => await context.Set<ArchivedProgramme>()
            .Where(programme => programme.NetworkId == networkId && programme.ServiceId == serviceId)
            .ExecuteDeleteAsync(cancellationToken);
}
