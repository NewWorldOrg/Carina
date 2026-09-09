namespace Carina.Domain.Streaming;

public interface ILiveSessionLedger
{
    Task<IReadOnlyList<LiveSessionView>> RunningAsync(CancellationToken cancellationToken);
}

public sealed record LiveSessionView
{
    public LiveSessionView(
        LiveSessionKey key,
        int viewers,
        LiveStartup startup,
        long dropped,
        int queued,
        IReadOnlyList<LiveBacklog>? watching = null,
        long? chunksDroppedSinceTheSupplyOpened = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentOutOfRangeException.ThrowIfNegative(viewers);
        ArgumentOutOfRangeException.ThrowIfNegative(dropped);
        ArgumentOutOfRangeException.ThrowIfNegative(queued);

        if (chunksDroppedSinceTheSupplyOpened is { } lost)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(lost);
        }

        Key = key;
        Viewers = viewers;
        Startup = startup;
        Dropped = dropped;
        Queued = queued;
        Watching = watching ?? [];
        ChunksDroppedSinceTheSupplyOpened = chunksDroppedSinceTheSupplyOpened;
    }

    public LiveSessionKey Key { get; }

    public int Viewers { get; }

    public LiveStartup Startup { get; }

    public long Dropped { get; }

    public int Queued { get; }

    public IReadOnlyList<LiveBacklog> Watching { get; }

    public long? ChunksDroppedSinceTheSupplyOpened { get; }
}
