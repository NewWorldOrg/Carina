namespace Carina.Domain.Streaming;

/// <summary>
/// One reader of a channel as it is received, taking neither a transcoder nor a transcoding seat.
/// </summary>
/// <remarks>
/// A reader that stops taking bytes is not waited for. What it cannot hold is dropped oldest first,
/// the way the driver drops for a viewer that has fallen behind, because the reading it sits on is
/// the one every other viewer of that channel is fed from and one external player must not be able
/// to put back pressure on it. What was dropped is counted here rather than against a session,
/// because a reader of the channel as it is asks for no profile and so has no
/// <see cref="LiveSessionKey"/> to be listed under on the running sessions.
/// </remarks>
public interface ILiveHandedOver : IAsyncDisposable
{
    Stream Bytes { get; }

    long ChunksDroppedSinceTheSupplyOpened { get; }

    /// <summary>
    /// Waits for the first mouthful of the channel to arrive, so that nothing is answered 200 on a
    /// reading that turns out to have nothing behind it.
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
