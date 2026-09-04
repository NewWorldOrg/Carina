using Carina.Contracts;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveLeases : ILiveLeases
{
    private readonly Lock gate = new();

    private readonly HashSet<SessionId> held = [];

    public IReadOnlyCollection<SessionId> Held
    {
        get
        {
            lock (gate)
            {
                return [.. held];
            }
        }
    }

    public void Take(SessionId session)
    {
        lock (gate)
        {
            held.Add(session);
        }
    }

    public void LetGo(SessionId session)
    {
        lock (gate)
        {
            held.Remove(session);
        }
    }
}
