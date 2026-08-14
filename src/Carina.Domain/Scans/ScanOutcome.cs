namespace Carina.Domain.Scans;

public sealed record ScanOutcome
{
    private ScanOutcome(
        ScanRun? run,
        ScanRunId? alreadyRunning,
        string? couldNotStart,
        IReadOnlyList<ScanRunAttempt> attempts,
        ScanDifference difference)
    {
        Run = run;
        AlreadyRunning = alreadyRunning;
        CouldNotStartBecause = couldNotStart;
        Attempts = attempts;
        Difference = difference;
    }

    public ScanRun? Run { get; }

    public ScanRunId? AlreadyRunning { get; }

    public string? CouldNotStartBecause { get; }

    public IReadOnlyList<ScanRunAttempt> Attempts { get; }

    public ScanDifference Difference { get; }

    public bool WasStarted => Run is not null;

    public ScanRunState? State => Run?.State;

    public IReadOnlyList<ScanRunAttempt> Failures => [.. Attempts.Where(attempt => attempt.Failed)];

    public static ScanOutcome Of(
        ScanRun run,
        IReadOnlyList<ScanRunAttempt> attempts,
        ScanDifference difference)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(difference);

        return new ScanOutcome(run, null, null, attempts, difference);
    }

    public static ScanOutcome RefusedBecauseOneIsRunning(ScanRunId? alreadyRunning)
        => new(null, alreadyRunning, null, [], ScanDifference.Nothing);

    public static ScanOutcome CouldNotStart(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ScanOutcome(null, null, reason, [], ScanDifference.Nothing);
    }
}
