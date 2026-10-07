using System.Globalization;

using Carina.Domain.Machines;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Where a recorded file's own clock begins, as ffprobe reads it from the file's head when asked
/// with <see cref="FfprobeInvocation.Start"/>.
/// </summary>
public static class FfprobeFileStart
{
    public const string Key = "start_time";

    public static Task<ProgrammeSaid> AskAsync(MachineSettings machine, string source, TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return AnotherProgramme.SayAsync(machine.Prober, FfprobeInvocation.Start(new StreamSource(source)), machine.LongestRead, clock, cancellationToken);
    }

    public static TimeSpan? Of(ProgrammeSaid said)
    {
        ArgumentNullException.ThrowIfNull(said);

        if (said.ExitCode is not 0)
        {
            return null;
        }

        string? named = said.Said
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith(Key + "=", StringComparison.Ordinal))
            .Select(line => line[(Key.Length + 1)..])
            .FirstOrDefault();

        return double.TryParse(named, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}
