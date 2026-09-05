using System.Globalization;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads how long a source is by asking ffprobe for one key and reading that key back out of the
/// answer. ffprobe complains about a broadcast recording all the way through and still exits 0,
/// measured on 2026-09-05, so what it said on the error stream decides nothing here.
/// </summary>
public sealed class FfprobeSourceLength(MachineSettings settings, TimeProvider clock) : ISourceLengthReader
{
    public const string Key = "duration";

    public async Task<SourceLengthReading> ReadAsync(string source, CancellationToken cancellationToken)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            settings.Prober,
            FfprobeLengthInvocation.Arguments(source),
            settings.LongestRead,
            clock,
            cancellationToken);

        if (said.Fault is ProgrammeFault.ProgrammeMissing)
        {
            return SourceLengthReading.Unanswered(SourceLengthFault.ProgrammeMissing, said.Complained);
        }

        if (said.Fault is ProgrammeFault.TimedOut)
        {
            return SourceLengthReading.Unanswered(SourceLengthFault.TimedOut, said.Complained);
        }

        if (said.ExitCode is not 0)
        {
            return SourceLengthReading.Refused(said.ExitCode!.Value, said.Complained);
        }

        return Measured(said.Said) is { } length
            ? SourceLengthReading.Read(length)
            : SourceLengthReading.Unanswered(
                SourceLengthFault.SaidNothing,
                $"the programme exited 0 and named no '{Key}' this could be read as a length");
    }

    private static TimeSpan? Measured(string answer)
    {
        foreach (FfprobeRecord record in FfprobeRecords.From(answer))
        {
            if (record.Value(Key) is { } said && Parsed(said) is { } length)
            {
                return length;
            }
        }

        return null;
    }

    private static TimeSpan? Parsed(string said)
        => double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
