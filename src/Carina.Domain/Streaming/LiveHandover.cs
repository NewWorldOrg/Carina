namespace Carina.Domain.Streaming;

/// <summary>
/// One reader of a channel as it is received, taking neither a transcoder nor a transcoding seat.
/// </summary>
/// <remarks>
/// A reader that stops taking bytes is not waited for: what it cannot hold is dropped oldest first,
/// and what was dropped is counted here rather than against a session.
/// </remarks>
public interface ILiveHandedOver : IAsyncDisposable
{
    Stream Bytes { get; }

    long ChunksDroppedSinceTheSupplyOpened { get; }

    /// <summary>
    /// Waits for the first bytes of the channel to arrive.
    /// </summary>
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
