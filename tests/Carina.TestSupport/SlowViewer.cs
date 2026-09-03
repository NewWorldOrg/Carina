using Carina.Domain.Streaming;

namespace Carina.TestSupport;

public sealed record SlowViewing(int Received, LiveBacklog Backlog);

public sealed class SlowViewer
{
    public static readonly TimeSpan MeasuredPace = TimeSpan.FromMilliseconds(400);

    private readonly TimeSpan pause;

    private readonly int? stopsAfter;

    private readonly TimeProvider clock;

    private SlowViewer(TimeSpan pause, int? stopsAfter, TimeProvider clock)
    {
        this.pause = pause;
        this.stopsAfter = stopsAfter;
        this.clock = clock;
    }

    public static SlowViewer ReadingOneEvery(TimeSpan pause, TimeProvider? clock = null)
    {
        if (pause < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pause), pause, "A viewer pauses for a while or not at all, never for less.");
        }

        return new SlowViewer(pause, null, clock ?? TimeProvider.System);
    }

    public static SlowViewer StallingAfter(int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);

        return new SlowViewer(TimeSpan.Zero, frames, TimeProvider.System);
    }

    public async Task<SlowViewing> WatchAsync(ILiveViewing viewing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewing);

        int received = 0;

        try
        {
            while (received != stopsAfter && await viewing.Frames.WaitToReadAsync(cancellationToken))
            {
                while (received != stopsAfter && viewing.Frames.TryRead(out LiveFrame? _))
                {
                    received++;

                    if (pause > TimeSpan.Zero)
                    {
                        await Task.Delay(pause, clock, cancellationToken);
                    }
                }
            }

            if (received == stopsAfter)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new SlowViewing(received, viewing.Backlog);
        }

        return new SlowViewing(received, viewing.Backlog);
    }
}
