using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public enum SourceHeadFault
{
    ProgrammeMissing = 1,

    TimedOut = 2,

    Refused = 3,

    SaidNothing = 4,
}

/// <summary>
/// Where a source begins and where its first decodable picture lies, both on the source's own clock.
/// The head skip is the distance between them (BR-ED2-006): the packet a stream says it starts at
/// is not it, because the pictures before the first I frame cannot be decoded and the run drops
/// them. A head that cannot be read is a reading with a fault, not a zero, because a zero would
/// be believed.
/// </summary>
public sealed record SourceHeadReading
{
    private SourceHeadReading(TimeSpan? start, TimeSpan? firstPicture, SourceHeadFault? fault, int? exitCode, string note)
    {
        Start = start;
        FirstPicture = firstPicture;
        Fault = fault;
        ExitCode = exitCode;
        Note = note;
    }

    public TimeSpan? Start { get; }

    public TimeSpan? FirstPicture { get; }

    public SourceHeadFault? Fault { get; }

    public int? ExitCode { get; }

    public string Note { get; }

    public bool Measured => Start is not null && FirstPicture is not null;

    public TimeSpan? HeadSkip => Start is { } start && FirstPicture is { } first ? first - start : null;

    public static SourceHeadReading Read(TimeSpan start, TimeSpan firstPicture)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, TimeSpan.Zero, nameof(start));

        if (firstPicture < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstPicture),
                firstPicture,
                "The first picture of a source is not before the source begins.");
        }

        return new SourceHeadReading(start, firstPicture, null, null, string.Empty);
    }

    public static SourceHeadReading Refused(int exitCode, string note)
    {
        if (exitCode is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "A programme that exited 0 was not refused by it.");
        }

        return new SourceHeadReading(null, null, SourceHeadFault.Refused, exitCode, Shortened(note));
    }

    public static SourceHeadReading Unanswered(SourceHeadFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A head goes unread for one of the reasons named here.");
        }

        if (fault is SourceHeadFault.Refused)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                $"A programme that refused says with what code, so {nameof(Refused)} takes one.");
        }

        return new SourceHeadReading(null, null, fault, null, Shortened(note));
    }

    private static string Shortened(string note) => ProgrammeNote.Of(note, ProgrammeNote.Longest);
}
