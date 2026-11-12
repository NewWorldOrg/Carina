namespace Carina.Domain.Scans;

/// <summary>
/// Why a scan's difference is not applied.
/// </summary>
public enum ScanApplyRefusal
{
    StillRunning = 1,

    NeverCompleted = 2,
}
