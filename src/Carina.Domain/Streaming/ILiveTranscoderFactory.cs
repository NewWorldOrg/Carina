using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveTranscoderFactory
{
    Task<LiveTranscoderStart> StartAsync(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        CancellationToken cancellationToken);
}
