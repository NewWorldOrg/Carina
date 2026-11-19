using Carina.Domain.Base;

namespace Carina.Domain.Machines;

public sealed record RunningProgramme
{
    public RunningProgramme(int processId, DateTime startedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(processId, 1);

        ProcessId = processId;
        StartedAt = UtcTimes.Required(startedAt, nameof(startedAt));
    }

    public int ProcessId { get; }

    public DateTime StartedAt { get; }

    public bool IsTheSameAs(DateTime startedAt, TimeSpan tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        return (UtcTimes.Required(startedAt, nameof(startedAt)) - StartedAt).Duration() <= tolerance;
    }

    public RunningProgramme NoEarlierThan(DateTime moment)
        => UtcTimes.Required(moment, nameof(moment)) > StartedAt
            ? new RunningProgramme(ProcessId, moment)
            : this;
}
