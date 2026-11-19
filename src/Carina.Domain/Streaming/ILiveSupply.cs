using Carina.Domain.Channels;

using Puppis.Contracts;

namespace Carina.Domain.Streaming;

public interface ILiveSupply
{
    Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<SessionId, long>> DroppedOnTheWayInAsync(CancellationToken cancellationToken);
}

public interface ILiveTransportStream : IAsyncDisposable
{
    SessionId Supply { get; }

    Stream Bytes { get; }

    LiveSupplyEnding? Ending { get; }

    Task<bool> HoldOpenUntilAsync(DateTimeOffset until, CancellationToken cancellationToken);
}
