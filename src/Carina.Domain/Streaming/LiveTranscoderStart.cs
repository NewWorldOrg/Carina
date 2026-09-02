namespace Carina.Domain.Streaming;

public sealed class LiveTranscoderStart
{
    private LiveTranscoderStart(
        ILiveTranscoder? transcoder,
        TranscoderFault? fault,
        TranscodeCeiling? ceiling,
        string note)
    {
        Transcoder = transcoder;
        Fault = fault;
        Ceiling = ceiling;
        Note = note;
    }

    public ILiveTranscoder? Transcoder { get; }

    public TranscoderFault? Fault { get; }

    public TranscodeCeiling? Ceiling { get; }

    public string Note { get; }

    public bool Running => Transcoder is not null;

    public static LiveTranscoderStart Started(ILiveTranscoder transcoder)
    {
        ArgumentNullException.ThrowIfNull(transcoder);

        return new LiveTranscoderStart(transcoder, null, null, string.Empty);
    }

    public static LiveTranscoderStart Failed(TranscoderFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A transcoder fails in one of the ways named here.");
        }

        return new LiveTranscoderStart(null, fault, null, TranscoderNote.Of(note));
    }

    public static LiveTranscoderStart Refused(TranscodeCeiling ceiling)
    {
        ArgumentNullException.ThrowIfNull(ceiling);

        return new LiveTranscoderStart(null, TranscoderFault.TooManyAlready, ceiling, ceiling.Said);
    }
}
