namespace Carina.Domain.Streaming;

public sealed record LiveBacklog
{
    public static readonly LiveBacklog Empty = new(0, 0L);

    public LiveBacklog(int queued, long dropped)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(queued);
        ArgumentOutOfRangeException.ThrowIfNegative(dropped);

        Queued = queued;
        Dropped = dropped;
    }

    public int Queued { get; }

    public long Dropped { get; }
}
