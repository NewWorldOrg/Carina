using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed class HeldSoundOnAir : ISoundOnAir
{
    public AnnouncedSound Announced { get; set; }

    public Task<AnnouncedSound> AnnouncedAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
        => Task.FromResult(Announced);
}
