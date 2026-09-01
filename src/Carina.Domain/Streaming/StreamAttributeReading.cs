namespace Carina.Domain.Streaming;

public enum StreamAttribute
{
    Resolution = 1,

    Scan = 2,

    FrameRate = 3,

    Audio = 4,
}

public enum StreamProbeFault
{
    ProgrammeMissing = 1,

    TimedOut = 2,

    Refused = 3,

    SaidNothing = 4,
}

public sealed record StreamAttributeReading
{
    public const int LongestNote = 500;

    private static readonly IReadOnlyList<StreamAttribute> Everything =
        [.. Enum.GetValues<StreamAttribute>()];

    private StreamAttributeReading(
        StreamAttributes attributes,
        IReadOnlyList<StreamAttribute> fellBackOn,
        bool severalVideoDescriptions,
        StreamProbeFault? fault,
        int? exitCode,
        string note)
    {
        Attributes = attributes;
        FellBackOn = fellBackOn;
        SeveralVideoDescriptions = severalVideoDescriptions;
        Fault = fault;
        ExitCode = exitCode;
        Note = note;
    }

    public StreamAttributes Attributes { get; }

    public IReadOnlyList<StreamAttribute> FellBackOn { get; }

    public bool SeveralVideoDescriptions { get; }

    public StreamProbeFault? Fault { get; }

    public int? ExitCode { get; }

    public string Note { get; }

    public bool Measured => FellBackOn.Count is 0;

    public bool FellBack(StreamAttribute attribute) => FellBackOn.Contains(attribute);

    public static StreamAttributeReading Read(
        StreamAttributes attributes,
        IEnumerable<StreamAttribute> fellBackOn,
        bool severalVideoDescriptions = false)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(fellBackOn);

        StreamAttribute[] guessed = [.. fellBackOn.Distinct().Order()];

        foreach (StreamAttribute attribute in guessed)
        {
            if (!Enum.IsDefined(attribute))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(fellBackOn),
                    attribute,
                    "A stream is read for the attributes named here and no others.");
            }
        }

        return new StreamAttributeReading(attributes, guessed, severalVideoDescriptions, null, null, string.Empty);
    }

    public static StreamAttributeReading Refused(int exitCode, string note)
    {
        if (exitCode is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "A programme that exited 0 was not refused by it.");
        }

        return new StreamAttributeReading(
            StreamAttributes.SafeSide,
            Everything,
            severalVideoDescriptions: false,
            StreamProbeFault.Refused,
            exitCode,
            Shortened(note));
    }

    public static StreamAttributeReading Unanswered(StreamProbeFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(nameof(fault), fault, "A probe fails in one of the ways named here.");
        }

        if (fault is StreamProbeFault.Refused)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                $"A programme that refused says with what code, so {nameof(Refused)} takes one.");
        }

        return new StreamAttributeReading(
            StreamAttributes.SafeSide,
            Everything,
            severalVideoDescriptions: false,
            fault,
            null,
            Shortened(note));
    }

    private static string Shortened(string note)
    {
        ArgumentNullException.ThrowIfNull(note);

        string trimmed = note.Trim();

        return trimmed.Length <= LongestNote ? trimmed : trimmed[^LongestNote..];
    }
}
