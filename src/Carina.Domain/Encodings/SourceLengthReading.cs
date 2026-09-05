using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public enum SourceLengthFault
{
    ProgrammeMissing = 1,

    TimedOut = 2,

    Refused = 3,

    SaidNothing = 4,
}

/// <summary>
/// How long the source is, which is the whole a job's progress is measured against. It is a
/// reading and not a number, because a source that cannot be measured must leave the job running
/// with no percentage rather than with a wrong one (BR-ED2-013).
/// </summary>
public sealed record SourceLengthReading
{
    private SourceLengthReading(TimeSpan? length, SourceLengthFault? fault, int? exitCode, string note)
    {
        Length = length;
        Fault = fault;
        ExitCode = exitCode;
        Note = note;
    }

    public TimeSpan? Length { get; }

    public SourceLengthFault? Fault { get; }

    public int? ExitCode { get; }

    public string Note { get; }

    public bool Measured => Length is not null;

    public static SourceLengthReading Read(TimeSpan length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero, nameof(length));

        return new SourceLengthReading(length, null, null, string.Empty);
    }

    public static SourceLengthReading Refused(int exitCode, string note)
    {
        if (exitCode is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "A programme that exited 0 was not refused by it.");
        }

        return new SourceLengthReading(null, SourceLengthFault.Refused, exitCode, Shortened(note));
    }

    public static SourceLengthReading Unanswered(SourceLengthFault fault, string note)
    {
        if (!Enum.IsDefined(fault))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                "A source goes unmeasured for one of the reasons named here.");
        }

        if (fault is SourceLengthFault.Refused)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fault),
                fault,
                $"A programme that refused says with what code, so {nameof(Refused)} takes one.");
        }

        return new SourceLengthReading(null, fault, null, Shortened(note));
    }

    private static string Shortened(string note) => ProgrammeNote.Of(note, ProgrammeNote.Longest);
}
