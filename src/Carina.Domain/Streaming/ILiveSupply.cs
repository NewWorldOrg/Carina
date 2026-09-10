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
    /// </summary>
    /// <remarks>
    /// The window a viewing is given exists so that a supply nobody is behind any more is let go of;
    /// asking again is how a viewing that is still being watched says it is still there. A supply
    /// already held that far is left alone rather than asked again.
    /// </remarks>
    Task<bool> HoldOpenUntilAsync(DateTimeOffset until, CancellationToken cancellationToken);
}
