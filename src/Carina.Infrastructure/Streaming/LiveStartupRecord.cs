using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveStartupRecord(TimeProvider clock) : ILiveStartup
{
    private readonly Lock gate = new();

    private readonly long began = clock.GetTimestamp();

    private LiveStartup current = LiveStartup.NotStarted;

    public LiveStartup? Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public void Reach(LiveStartupSegment segment)
    {
        lock (gate)
        {
            if (current.Reached(segment))
            {
                return;
            }

            current = current.Reaching(segment, clock.GetElapsedTime(began));
        }
    }
}
