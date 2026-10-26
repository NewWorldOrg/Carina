using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveTranscoderFactory
{
    Task<LiveTranscoderStart> StartAsync(
        ServiceId service,
        LiveProfile profile,
        VideoCodec format,
        SoundPlacement sound,
        StreamAttributes attributes,
        CaptionOutlet captions,
        CancellationToken cancellationToken);
}
