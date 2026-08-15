namespace Carina.Domain.Scans;

public interface IScanRunObserver
{
    ScanStop Stop { get; }

    void Started(ScanRun run);
}

public sealed class UnwatchedScanRun : IScanRunObserver
{
    public static readonly UnwatchedScanRun Instance = new();

    private UnwatchedScanRun()
    {
    }

    public ScanStop Stop => ScanStop.AsRequested;

    public void Started(ScanRun run)
    {
    }
}
