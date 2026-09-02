namespace Carina.Domain.Streaming;

public sealed class LiveSupplyStart
{
    private LiveSupplyStart(ILiveTransportStream? stream, LiveRefusal? refusal, string note)
    {
        Stream = stream;
        Refusal = refusal;
        Note = note;
    }

    public ILiveTransportStream? Stream { get; }

    public LiveRefusal? Refusal { get; }

    public string Note { get; }

    public bool Flowing => Stream is not null;

    public static LiveSupplyStart Opened(ILiveTransportStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new LiveSupplyStart(stream, null, string.Empty);
    }

    public static LiveSupplyStart Refused(LiveRefusal refusal, string note)
    {
        if (!LiveRefusals.FromTheSupply.Contains(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A supply refuses for one of the reasons a tuner can have, and says nothing about transcoding.");
        }

        return new LiveSupplyStart(null, refusal, TranscoderNote.Of(note));
    }
}
