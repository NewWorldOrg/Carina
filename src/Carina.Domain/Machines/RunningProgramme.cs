using Carina.Domain.Base;

namespace Carina.Domain.Machines;

/// <summary>
/// A programme this process started, as the operating system knows it: the process id and the
/// moment it began. The id alone is not an identity — the kernel hands it out again once the
/// programme is gone — so the two are only ever kept and compared together (BR-ED2-011).
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
    /// Whether a programme found under this id now is the one written down: it began when the
    /// written one began, give or take what the clock the start time is read from can drift by
    /// between two readings.
    /// </summary>
    public bool IsTheSameAs(DateTime startedAt, TimeSpan tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tolerance, TimeSpan.Zero);

        return (UtcTimes.Required(startedAt, nameof(startedAt)) - StartedAt).Duration() <= tolerance;
    }
}
