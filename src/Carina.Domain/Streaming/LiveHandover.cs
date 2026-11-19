namespace Carina.Domain.Streaming;

public interface ILiveHandedOver : IAsyncDisposable
{
    Stream Bytes { get; }

    long ChunksDroppedSinceTheSupplyOpened { get; }

    ValueTask<bool> ReachedAsync(CancellationToken cancellationToken);
}

public sealed class LiveHandover
{
    private LiveHandover(ILiveHandedOver? handed, LiveRefusal? refusal)
    {
        Handed = handed;
        Refusal = refusal;
    }

    public ILiveHandedOver? Handed { get; }

    public LiveRefusal? Refusal { get; }

    public static LiveHandover Carrying(ILiveHandedOver handed)
    {
        ArgumentNullException.ThrowIfNull(handed);

        return new LiveHandover(handed, null);
    }

    public static LiveHandover Refused(LiveRefusal refusal)
    {
        if (!LiveRefusals.FromTheSupply.Contains(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A channel handed over as it is takes no transcoder, so it is refused only for a reason a tuner can have.");
        }

        return new LiveHandover(null, refusal);
    }
}
