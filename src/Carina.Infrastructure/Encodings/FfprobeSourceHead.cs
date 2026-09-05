using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Reads where a source begins and where its first decodable picture lies by asking ffprobe for two
/// keys and reading each back by its key (BR-ED2-013). The first picture is the first frame ffprobe
/// decoded, not the first packet a stream declares: measured on 2026-09-05 the two lie 0.133 s apart
/// on one recording and the container's start a further 0.374 s before that. What ffprobe complained
/// about on the way decides nothing here, as it complains about every broadcast recording.
/// </summary>
public sealed class FfprobeSourceHead(MachineSettings settings, TimeProvider clock) : ISourceHeadReader
{
    public const string StartKey = "start_time";

    public const string PictureKey = "best_effort_timestamp_time";

    public async Task<SourceHeadReading> ReadAsync(string source, ServiceId service, CancellationToken cancellationToken)
    {
        ProgrammeSaid said = await AnotherProgramme.SayAsync(
            settings.Prober,
            FfprobeHeadInvocation.Arguments(source, service),
            settings.LongestRead,
            clock,
            cancellationToken);

        if (said.Fault is ProgrammeFault.ProgrammeMissing)
        {
            return SourceHeadReading.Unanswered(SourceHeadFault.ProgrammeMissing, said.Complained);
        }

        if (said.Fault is ProgrammeFault.TimedOut)
        {
            return SourceHeadReading.Unanswered(SourceHeadFault.TimedOut, said.Complained);
        }

        if (said.ExitCode is not 0)
        {
            return SourceHeadReading.Refused(said.ExitCode!.Value, said.Complained);
        }

        IReadOnlyList<FfprobeRecord> records = FfprobeRecords.From(said.Said);
        TimeSpan? start = First(records, StartKey);
        TimeSpan? picture = First(records, PictureKey);

        if (start is not { } begins)
        {
            return SourceHeadReading.Unanswered(
                SourceHeadFault.SaidNothing,
                $"the programme exited 0 and named no '{StartKey}' this could be read as where the source begins");
        }

        if (picture is not { } first)
        {
            return SourceHeadReading.Unanswered(
                SourceHeadFault.SaidNothing,
                $"the programme exited 0 and decoded no picture in the first {FfprobeHeadInvocation.ReadFor.TotalSeconds:0} s of the source");
        }

        if (first < begins)
        {
            return SourceHeadReading.Unanswered(
                SourceHeadFault.SaidNothing,
                $"the programme placed the first picture at {first.TotalSeconds:0.######} s, before the source begins at {begins.TotalSeconds:0.######} s");
        }

        return SourceHeadReading.Read(begins, first);
    }

    private static TimeSpan? First(IReadOnlyList<FfprobeRecord> records, string key)
    {
        foreach (FfprobeRecord record in records)
        {
            if (record.Value(key) is { } said && Parsed(said) is { } moment)
            {
                return moment;
            }
        }

        return null;
    }

    private static TimeSpan? Parsed(string said)
        => double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && seconds >= 0
            && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
}
