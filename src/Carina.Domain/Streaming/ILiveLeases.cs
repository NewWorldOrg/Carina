using Puppis.Contracts;

namespace Carina.Domain.Streaming;

public interface ILiveLeases
{
    IReadOnlyCollection<SessionId> Held { get; }

    void Take(SessionId session);

    void LetGo(SessionId session);
}
