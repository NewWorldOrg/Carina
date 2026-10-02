using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class SoundOnAir(IProgrammeRepository programmes, TimeProvider clock) : ISoundOnAir
{
    private static readonly TimeSpan AMoment = TimeSpan.FromTicks(1);

    public async Task<AnnouncedSound> AnnouncedAsync(
        NetworkId network,
        ServiceId service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<Programme> listed = await programmes.ListAsync(
            new ProgrammeWindow(network.Value, service.Value, now, now + AMoment),
            cancellationToken);
        Programme? onAir = listed
            .Where(programme => !programme.IsShadow
                                && programme.StartsAt <= now
                                && (programme.EndsAt is not { } ends || ends > now))
            .OrderByDescending(programme => programme.StartsAt)
            .FirstOrDefault();

        return onAir is null ? default : new AnnouncedSound(onAir.Audio, onAir.Sounds);
    }
}
