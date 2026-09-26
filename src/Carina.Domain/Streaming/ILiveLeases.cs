using Carina.Contracts;

namespace Carina.Domain.Streaming;

/// <summary>
/// What this app is holding on the driver for live viewing.
/// </summary>
/// <remarks>
/// The lease is taken before the driver is told the session id, so a live session the driver holds
/// and this list does not name is a stray.
/// </remarks>
public interface ILiveLeases
{
    IReadOnlyCollection<SessionId> Held { get; }

    void Take(SessionId session);

    void LetGo(SessionId session);
}
