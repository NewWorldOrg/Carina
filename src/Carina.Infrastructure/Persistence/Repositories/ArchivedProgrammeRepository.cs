using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Carina.Infrastructure.Persistence.Configurations;

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
            .Where(programme => !EF.Property<bool>(programme, ArchivedProgrammeConfiguration.Superseded))
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => programme.StartsAt < to && programme.EndsAt > from)
            .OrderBy(programme => programme.StartsAt)
            .ThenBy(programme => programme.EventId)
            .ToListAsync(cancellationToken);
        HashSet<(int NetworkId, int ServiceId)> wanted =
            services.Select(service => (service.NetworkId, service.ServiceId)).ToHashSet();

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
        await JudgeAsync(programmes, cancellationToken);

        return kept;
    }

    /// <summary>
    /// Writes down, for the programmes just kept and the archived programmes they share airtime with, whether a
    /// programme heard later has superseded them. Every programme of their services that could share airtime with
    /// one of them is read, in the archive and in the guide, and judged by <see cref="ProgrammeSupersession"/>.
    /// </summary>
    private async Task JudgeAsync(IReadOnlyList<ArchivedProgramme> kept, CancellationToken cancellationToken)
    {
        if (kept.Count == 0)
        {
            return;
        }

        HashSet<(int Network, int Service)> services =
            [.. kept.Select(programme => (programme.NetworkId.Value, programme.ServiceId.Value))];
        IReadOnlyList<ArchivedProgramme> judged = await ArchivedOverAsync(
            services,
            kept.Min(programme => programme.StartsAt),
            kept.Max(programme => programme.EndsAt),
            cancellationToken);
        DateTime from = judged.Min(programme => programme.StartsAt);
        DateTime to = judged.Max(programme => programme.EndsAt);
        IReadOnlyList<ArchivedProgramme> archived = await ArchivedOverAsync(services, from, to, cancellationToken);
        HashSet<ArchivedProgramme> standing =
            [.. ProgrammeSupersession.Standing(archived, await HeldOverAsync(services, from, to, cancellationToken))];

        foreach (ArchivedProgramme programme in judged)
        {
            context.Entry(programme).Property<bool>(ArchivedProgrammeConfiguration.Superseded).CurrentValue =
                !standing.Contains(programme);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ArchivedProgramme>> ArchivedOverAsync(
        HashSet<(int Network, int Service)> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        List<NetworkId> networks = [.. services.Select(service => new NetworkId(service.Network)).Distinct()];
        List<ServiceId> carried = [.. services.Select(service => new ServiceId(service.Service)).Distinct()];
        List<ArchivedProgramme> found = await context.Set<ArchivedProgramme>()
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => programme.StartsAt < to && programme.EndsAt > from)
            .ToListAsync(cancellationToken);

        return [.. found.Where(programme => services.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))];
    }

    private async Task<IReadOnlyList<Programme>> HeldOverAsync(
        HashSet<(int Network, int Service)> services,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        List<NetworkId> networks = [.. services.Select(service => new NetworkId(service.Network)).Distinct()];
        List<ServiceId> carried = [.. services.Select(service => new ServiceId(service.Service)).Distinct()];
        DateTime unannouncedFrom = from - ProgrammeSupersession.LengthWhenTheEndIsNotAnnounced;
        List<Programme> found = await context.Set<Programme>()
            .Where(programme => networks.Contains(programme.NetworkId))
            .Where(programme => carried.Contains(programme.ServiceId))
            .Where(programme => !programme.IsShadow)
            .Where(programme => programme.StartsAt < to)
            .Where(programme => programme.EndsAt > from
                || (programme.EndsAt == null && programme.StartsAt > unannouncedFrom))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. found.Where(programme => services.Contains((programme.NetworkId.Value, programme.ServiceId.Value)))];
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
