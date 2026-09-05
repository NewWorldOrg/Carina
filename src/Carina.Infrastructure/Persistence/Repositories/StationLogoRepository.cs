using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class StationLogoRepository(CarinaDbContext context) : IStationLogoRepository
{
    public async Task<StationLogo?> FindAsync(
        NetworkId networkId,
        LogoId logoId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(logoId);

        return await context.Set<StationLogo>()
            .FirstOrDefaultAsync(
                logo => logo.NetworkId == networkId && logo.LogoId == logoId,
                cancellationToken);
    }

    public async Task<StationLogo?> OfServiceAsync(
        NetworkId networkId,
        ServiceId serviceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);

        return await context.Set<BroadcastService>()
            .Where(service => service.NetworkId == networkId && service.ServiceId == serviceId)
            .Join(
                context.Set<StationLogo>(),
                service => new { service.NetworkId, LogoId = service.LogoId! },
                logo => new { logo.NetworkId, logo.LogoId },
                (service, logo) => logo)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StationLogo>> ListAsync(CancellationToken cancellationToken)
        => await context.Set<StationLogo>()
            .OrderBy(logo => logo.NetworkId)
            .ThenBy(logo => logo.LogoId)
            .ToListAsync(cancellationToken);

    public async Task AbsorbAsync(StationLogo logo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(logo);

        StationLogo? held = await FindAsync(logo.NetworkId, logo.LogoId, cancellationToken);

        if (held is null)
        {
            context.Add(logo);
        }
        else if (!held.Absorb(logo))
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
