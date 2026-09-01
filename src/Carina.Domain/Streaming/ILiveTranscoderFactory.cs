namespace Carina.Domain.Streaming;

public interface ILiveTranscoderFactory
{
    Task<LiveTranscoderStart> StartAsync(
        LiveProfile profile,
        StreamAttributes attributes,
        CancellationToken cancellationToken);
}
