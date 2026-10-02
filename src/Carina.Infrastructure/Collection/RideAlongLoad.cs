namespace Carina.Infrastructure.Collection;

public readonly record struct RideAlongMark(long At, long Allocated);

/// <summary>
/// What riding along with one session has cost the application since the ride began: the bytes read, the time
/// spent reading them and what that allocated, and the time spent writing the guide down.
/// </summary>
public sealed class RideAlongLoad(TimeProvider clock)
{
    private readonly long began = clock.GetTimestamp();

    public long Bytes { get; private set; }

    public TimeSpan Reading { get; private set; }

    public long Allocated { get; private set; }

    public TimeSpan Writing { get; private set; }

    public TimeSpan Elapsed => clock.GetElapsedTime(began);

    public RideAlongMark Mark() => new(clock.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread());

    public void Read(RideAlongMark since, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        Bytes += bytes;
        Reading += clock.GetElapsedTime(since.At);
        Allocated += Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - since.Allocated);
    }

    public void Wrote(RideAlongMark since) => Writing += clock.GetElapsedTime(since.At);
}
