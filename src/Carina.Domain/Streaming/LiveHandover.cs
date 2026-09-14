namespace Carina.Domain.Streaming;

public interface ILiveHandedOver : IAsyncDisposable
{
    Stream Bytes { get; }
}

public sealed class LiveHandover
{
    private LiveHandover(ILiveHandedOver? handed, LiveRefusal? refusal, string note)
    {
        Handed = handed;
        Refusal = refusal;
        Note = note;
    }

    public ILiveHandedOver? Handed { get; }

    public LiveRefusal? Refusal { get; }

    public string Note { get; }

    public static LiveHandover Carrying(ILiveHandedOver handed)
    {
        ArgumentNullException.ThrowIfNull(handed);

        return new LiveHandover(handed, null, string.Empty);
    }

    public static LiveHandover Refused(LiveRefusal refusal, string note)
    {
        if (!LiveRefusals.FromTheSupply.Contains(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A channel handed over as it is takes no transcoder, so it is refused only for a reason a tuner can have.");
        }

        return new LiveHandover(null, refusal, TranscoderNote.Of(note));
    }
}
