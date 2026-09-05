using Carina.Domain.Channels;

namespace Carina.Infrastructure.Logos;

public sealed record LogosWritten(int Pictures, int Stations, int NoPicture);

public sealed class LogoWriter(
    IStationLogoRepository logos,
    IBroadcastServiceRepository services,
    TimeProvider clock)
{
    public async Task<LogosWritten> WriteAsync(LogoVisitResult visit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visit);

        DateTime at = clock.GetUtcNow().UtcDateTime;
        int pictures = 0;
        int stations = 0;
        int silent = 0;

        foreach (HarvestedLogo found in visit.Logos)
        {
            await logos.AbsorbAsync(
                StationLogo.Collect(
                    new NetworkId(found.NetworkId),
                    new LogoId(found.LogoId),
                    found.LogoType,
                    found.LogoVersion,
                    found.Image.Width,
                    found.Image.Height,
                    found.Image.Bytes.ToArray(),
                    at),
                cancellationToken);

            pictures++;
        }

        foreach (HarvestedLogoLink link in visit.Links)
        {
            if (await services.FindAsync(
                    new NetworkId(link.NetworkId),
                    new ServiceId(link.ServiceId),
                    cancellationToken) is not { } service)
            {
                continue;
            }

            bool moved = link.LogoId is { } named
                ? service.NamesTheLogo(new LogoId(named))
                : service.BroadcastsNoLogo();

            if (!moved)
            {
                continue;
            }

            await services.SaveAsync(service, cancellationToken);

            if (link.LogoId is null)
            {
                silent++;
            }
            else
            {
                stations++;
            }
        }

        return new LogosWritten(pictures, stations, silent);
    }
}
