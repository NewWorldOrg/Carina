using Carina.Contracts;
using Carina.Domain.Channels;

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

    /// <summary>
    /// Asks that the supply be held open at least until the given time, and answers whether it now is.
    /// A supply already held that far is not asked again.
    /// </summary>
    Task<bool> HoldOpenUntilAsync(DateTimeOffset until, CancellationToken cancellationToken);
}
