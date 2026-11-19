using Carina.Domain.Machines;
using Carina.Infrastructure.Machines;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.DataBroadcast;

public sealed class FfprobeRecordingClockStart(MachineSettings machine, TimeProvider clock) : IRecordingClockStart
{
    public async Task<RecordingClockStartReading> ReadAsync(string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        ProgrammeSaid said = await FfprobeFileStart.AskAsync(machine, source, clock, cancellationToken);

        return new RecordingClockStartReading(
            FfprobeFileStart.Of(said),
            said.ExitCode is 0 ? $"the programme named no '{FfprobeFileStart.Key}' this could be read as where the file begins" : said.Complained);
    }
}
