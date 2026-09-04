namespace Carina.Domain.Streaming;

public sealed class LiveSupplyStart
{
    private LiveSupplyStart(
        ILiveTransportStream? stream,
        LiveRefusal? refusal,
        LiveRefusalDetail detail,
        string note)
    {
        Stream = stream;
        Refusal = refusal;
        Detail = detail;
        Note = note;
    }

    public ILiveTransportStream? Stream { get; }

    public LiveRefusal? Refusal { get; }

    public LiveRefusalDetail Detail { get; }

    public string Note { get; }

    public bool Flowing => Stream is not null;

    public static LiveSupplyStart Opened(ILiveTransportStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new LiveSupplyStart(stream, null, LiveRefusalDetail.Unsaid, string.Empty);
    }

    public static LiveSupplyStart Refused(LiveRefusal refusal, string note)
        => Refused(refusal, note, LiveRefusalDetail.Unsaid);

    public static LiveSupplyStart Refused(LiveRefusal refusal, string note, LiveRefusalDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        if (!LiveRefusals.FromTheSupply.Contains(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A supply refuses for one of the reasons a tuner can have, and says nothing about transcoding.");
        }

        if (!detail.Fits(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(detail),
                detail,
                $"A detail belongs to the reason it explains, and {refusal} does not take this one.");
        }

        return new LiveSupplyStart(null, refusal, detail, TranscoderNote.Of(note));
    }
}
