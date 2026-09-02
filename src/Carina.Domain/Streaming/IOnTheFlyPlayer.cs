using Carina.Domain.Channels;
using Carina.Domain.Playback;

namespace Carina.Domain.Streaming;

public interface IOnTheFlyPlayer
{
    Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken);
}
