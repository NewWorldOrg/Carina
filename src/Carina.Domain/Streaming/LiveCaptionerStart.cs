namespace Carina.Domain.Streaming;

public sealed class LiveCaptionerStart
{
    private LiveCaptionerStart(ILiveCaptioner? captioner, TranscoderFault? fault, string note)
    {
        Captioner = captioner;
        Fault = fault;
        Note = note;
    }

    public ILiveCaptioner? Captioner { get; }

    public TranscoderFault? Fault { get; }

    public string Note { get; }

    public bool Running => Captioner is not null;

    public static LiveCaptionerStart Started(ILiveCaptioner captioner)
    {
        ArgumentNullException.ThrowIfNull(captioner);

        return new LiveCaptionerStart(captioner, null, string.Empty);
    }

    public static LiveCaptionerStart Failed(TranscoderFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A captioner fails in one of the ways named here.");
        }

        return new LiveCaptionerStart(null, fault, TranscoderNote.Of(note));
    }
}
