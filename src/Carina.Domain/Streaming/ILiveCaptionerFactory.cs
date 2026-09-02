using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveCaptionerFactory
{
    Task<LiveCaptionerStart> StartAsync(ServiceId service, StreamAttributes attributes, CancellationToken cancellationToken);
}
