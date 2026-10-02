using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ISoundOnAir
{
    /// <summary>
    /// What the programme on air on this channel announces of its sound. A channel with nothing on air that the
    /// guide knows of has announced nothing.
    /// </summary>
    Task<AnnouncedSound> AnnouncedAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken);
}
