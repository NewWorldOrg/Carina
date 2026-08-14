namespace Carina.Domain.Scans;

public interface IScanRunObserver
{
    void Started(ScanRun run);
}

public sealed class UnwatchedScanRun : IScanRunObserver
{
    public static readonly UnwatchedScanRun Instance = new();

    private UnwatchedScanRun()
    {
    }

    public void Started(ScanRun run)
    {
    }
}
