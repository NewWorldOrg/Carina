using Carina.Domain.Channels;
using Carina.Domain.Playback;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Feature.Playback;

internal sealed class GatedOnTheFlyPlayer(byte[] bytes) : IOnTheFlyPlayer
{
    public GatedStream Handed { get; } = new(bytes);

    public Task<OnTheFlyStart> StartAsync(
        PlaybackFile file,
        ServiceId service,
        TimeSpan from,
        LiveProfile? profile,
        VideoCodec format,
        SoundPlacement sound,
        CancellationToken cancellationToken)
        => Task.FromResult(OnTheFlyStart.Started(new GatedViewing(Handed)));

    public Task<CarriedSounds> SoundsAsync(
        PlaybackFile file,
        ServiceId service,
        CancellationToken cancellationToken)
        => Task.FromResult(CarriedSounds.Counted(1));
}
