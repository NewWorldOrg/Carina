using Carina.Domain.Base;

namespace Carina.Domain.Machines;

/// <summary>
/// A programme this process started, as the operating system knows it: the process id and the
/// moment it began, kept and compared together.
/// </summary>
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

    /// <summary>
    /// Whether a programme found under this id now is the one written down: it began when the written
    /// one began, within <paramref name="tolerance"/>.
    /// </summary>
    public bool IsTheSameAs(DateTime startedAt, TimeSpan tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        return (UtcTimes.Required(startedAt, nameof(startedAt)) - StartedAt).Duration() <= tolerance;
    }

    /// <summary>
    /// This programme written as begun no earlier than <paramref name="moment"/>, because the operating
    /// system reports when a process began at a coarser resolution than the clock the caller keeps.
    /// </summary>
    public RunningProgramme NoEarlierThan(DateTime moment)
        => UtcTimes.Required(moment, nameof(moment)) > StartedAt
            ? new RunningProgramme(ProcessId, moment)
            : this;
}
