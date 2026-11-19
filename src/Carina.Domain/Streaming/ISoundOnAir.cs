using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ISoundOnAir
{
    Task<AnnouncedSound> AnnouncedAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken);
}
