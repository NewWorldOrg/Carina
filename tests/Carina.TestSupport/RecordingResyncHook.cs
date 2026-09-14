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

    public DriverHello? LastHello { get; private set; }

    public Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
    {
        LastHello = hello;

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
