namespace Carina.Domain.Scans;

public enum ScanRunState
{
    Running = 1,

    Completed = 2,

    Failed = 3,

    Cancelled = 4,

    Interrupted = 5,
}
