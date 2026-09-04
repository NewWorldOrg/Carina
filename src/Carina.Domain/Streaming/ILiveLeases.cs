using Carina.Contracts;

namespace Carina.Domain.Streaming;

/// <summary>
/// What this app is holding on the driver for live viewing.
/// </summary>
/// <remarks>
/// A live session on the driver exists to feed a viewer through this app and has nothing else to
/// protect, so one this app is not behind is holding a tuner for nobody. The app takes the lease
/// before the driver is ever told the session id, so a session the driver holds and this list does
/// not name is a stray rather than one that is halfway through being raised.
/// </remarks>
public interface ILiveLeases
{
    IReadOnlyCollection<SessionId> Held { get; }

    void Take(SessionId session);

    void LetGo(SessionId session);
}
