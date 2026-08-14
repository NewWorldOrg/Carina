namespace Carina.Domain.Scans;

public sealed record ScanRunStart
{
    private ScanRunStart(ScanRun? started, ScanRunId? alreadyRunning)
    {
        Started = started;
        AlreadyRunning = alreadyRunning;
    }

    public ScanRun? Started { get; }

    public ScanRunId? AlreadyRunning { get; }

    public bool WasStarted => Started is not null;

    public static ScanRunStart Of(ScanRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new ScanRunStart(run, null);
    }

    public static ScanRunStart RefusedBecauseOneIsRunning(ScanRunId? alreadyRunning)
        => new(null, alreadyRunning);
}
