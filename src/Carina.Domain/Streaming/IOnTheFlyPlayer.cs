using Carina.Domain.Channels;
using Carina.Domain.Playback;

namespace Carina.Domain.Streaming;

public interface IOnTheFlyPlayer
{
    Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile? profile,
        SoundTrack sound,
        CancellationToken cancellationToken);

    Task<CarriedSounds> SoundsAsync(PlaybackFile file, ServiceId service, CancellationToken cancellationToken);
}
