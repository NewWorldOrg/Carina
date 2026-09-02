using Carina.Domain.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveSupply
{
    Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken);
}

public interface ILiveTransportStream : IAsyncDisposable
{
    Stream Bytes { get; }

    LiveSupplyEnding? Ending { get; }
}
