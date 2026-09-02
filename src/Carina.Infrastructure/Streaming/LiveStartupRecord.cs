using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveStartupRecord(TimeProvider clock) : ILiveStartup
{
    private readonly Lock gate = new();

    private readonly long began = clock.GetTimestamp();

    private LiveStartup current = LiveStartup.NotStarted;

    private TaskCompletionSource advanced = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public Task Advanced
    {
        get
        {
            lock (gate)
            {
                return advanced.Task;
            }
        }
    }

    public void Reach(LiveStartupSegment segment)
    {
        TaskCompletionSource waited;

        lock (gate)
        {
            if (current.Reached(segment))
            {
                return;
            }

            current = current.Reaching(segment, clock.GetElapsedTime(began));
            waited = advanced;
            advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        waited.SetResult();
    }
}
