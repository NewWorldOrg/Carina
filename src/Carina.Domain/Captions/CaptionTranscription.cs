namespace Carina.Domain.Captions;

public enum CaptionFault
{
    ProgrammeMissing = 1,

    Refused = 2,

    TimedOut = 3,

    PicturesUnreadable = 4,

    ClockUnread = 5,

    CanvasUnread = 6,
}

public sealed record CaptionTranscription
{
    private CaptionTranscription(CaptionRecord? record, bool noCaptionStream, CaptionFault? fault, string note)
    {
        Record = record;
        NoCaptionStream = noCaptionStream;
        Fault = fault;
        Note = note;
    }

    public CaptionRecord? Record { get; }

    public bool NoCaptionStream { get; }

    public CaptionFault? Fault { get; }

    public string Note { get; }

    public bool DrewNothing => Fault is null && (NoCaptionStream || Record is null);

    public static CaptionTranscription Transcribed(CaptionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new CaptionTranscription(record, false, null, string.Empty);
    }

    public static CaptionTranscription NothingShown() => new(null, false, null, string.Empty);

    public static CaptionTranscription WithoutACaptionStream(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        return new CaptionTranscription(null, true, null, note);
    }

    public static CaptionTranscription Failed(CaptionFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A transcription fails in one of the ways named here.");
        }

        ArgumentNullException.ThrowIfNull(note);

        return new CaptionTranscription(null, false, fault, note);
    }
}
