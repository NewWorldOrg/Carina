namespace Carina.Domain.Streaming;

public interface ILiveSessionLedger
{
    IReadOnlyList<LiveSessionView> Running { get; }
}

public sealed record LiveSessionView
{
    public LiveSessionView(LiveSessionKey key, int viewers, LiveStartup startup, long dropped, int queued)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentOutOfRangeException.ThrowIfNegative(viewers);
        ArgumentOutOfRangeException.ThrowIfNegative(dropped);
        ArgumentOutOfRangeException.ThrowIfNegative(queued);

        Key = key;
        Viewers = viewers;
        Startup = startup;
        Dropped = dropped;
        Queued = queued;
    }

    public LiveSessionKey Key { get; }

    public int Viewers { get; }

    public LiveStartup Startup { get; }

    public long Dropped { get; }

    public int Queued { get; }
}
