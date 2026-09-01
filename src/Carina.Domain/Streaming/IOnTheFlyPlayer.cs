using Carina.Domain.Playback;

namespace Carina.Domain.Streaming;

public interface IOnTheFlyPlayer
{
    Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        TimeSpan from,
        LiveProfile profile,
        CancellationToken cancellationToken);
}
