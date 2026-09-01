namespace Carina.Domain.Streaming;

public enum TranscoderFault
{
    ProgrammeMissing = 1,

    Refused = 2,

    CalledOff = 3,
}

public sealed record TranscoderExit
{
    private TranscoderExit(TranscoderFault? fault, int? exitCode, string note)
    {
        Fault = fault;
        ExitCode = exitCode;
        Note = note;
    }

    public TranscoderFault? Fault { get; }

    public int? ExitCode { get; }

    public string Note { get; }

    public bool RanToTheEnd => Fault is null;

    public static TranscoderExit Finished() => new(null, 0, string.Empty);

    public static TranscoderExit Refused(int exitCode, string note)
    {
        if (exitCode is 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exitCode),
                exitCode,
                "A programme that exited 0 was not refused by it.");
        }

        return new TranscoderExit(TranscoderFault.Refused, exitCode, TranscoderNote.Of(note));
    }

    public static TranscoderExit CalledOff(string note)
        => new(TranscoderFault.CalledOff, null, TranscoderNote.Of(note));
}
