namespace Carina.Domain.Streaming;

public enum EncoderRefusal
{
    NodeMissing = 1,

    NodeUnreadable = 2,

    DriverUnusable = 3,

    ProbeTimedOut = 4,

    ProbeProgrammeMissing = 5,
}

public sealed record LiveEncoderChoice
{
    private LiveEncoderChoice(LiveEncoder encoder, EncoderRefusal? fellBackBecause, string note)
    {
        Encoder = encoder;
        FellBackBecause = fellBackBecause;
        Note = note;
    }

    public LiveEncoder Encoder { get; }

    public EncoderRefusal? FellBackBecause { get; }

    public string Note { get; }

    public bool FellBack => FellBackBecause is not null;

    public static LiveEncoderChoice Asked(LiveEncoder encoder)
    {
        if (!Enum.IsDefined(encoder))
        {
            throw new ArgumentOutOfRangeException(
                nameof(encoder),
                encoder,
                "A picture is encoded by one of the two the benchmark compared.");
        }

        return new LiveEncoderChoice(encoder, null, string.Empty);
    }

    public static LiveEncoderChoice FellBackToSoftware(EncoderRefusal because, string note)
    {
        if (!Enum.IsDefined(because))
        {
            throw new ArgumentOutOfRangeException(
                nameof(because),
                because,
                "A card is turned down for one of the reasons named here.");
        }

        return new LiveEncoderChoice(LiveEncoder.Software, because, TranscoderNote.Of(note));
    }
}
