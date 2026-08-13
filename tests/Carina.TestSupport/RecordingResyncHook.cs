using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.TestSupport;

public sealed class RecordingResyncHook : IDriverSessionResyncHook
{
    private readonly List<IReadOnlyList<SessionSnapshot>> calls = [];
    private readonly Lock gate = new();

    public int CallCount
    {
        get
        {
            lock (gate)
            {
                return calls.Count;
            }
        }
    }

    public IReadOnlyList<SessionSnapshot>? LastSessions
    {
        get
        {
            lock (gate)
            {
                return calls.Count > 0 ? calls[^1] : null;
            }
        }
    }

    public Exception? Failure { get; set; }

    public Task ReadoptAsync(
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
        {
            throw failure;
        }

        lock (gate)
        {
            calls.Add(sessions);
        }

        return Task.CompletedTask;
    }
}
