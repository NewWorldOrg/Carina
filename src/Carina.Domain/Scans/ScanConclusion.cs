namespace Carina.Domain.Scans;

public enum ScanStop
{
    AsRequested = 1,

    BecauseTheAppIsStopping = 2,
}

/// <summary>
/// How a walk over the targets ended: the driver was replaced under it, it was stopped, it failed for a reason, or
/// none of these, so it walked through.
/// </summary>
public sealed record ScanWalkEnd(bool DriverReplaced, ScanStop? Stopped, string? Failure)
{
    public bool WalkedThrough => !DriverReplaced && Stopped is null && Failure is null;
}

public static class ScanConclusion
{
    public const string CancelledReason = "the scan was cancelled";

    public const string AppStoppingReason = "the app stopped while this scan was walking";

    public const string AbandonedReason =
        "the app was not running this scan when it started, so it had been left behind by an earlier process";

    public static void Stop(ScanRun run, ScanStop stop, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (stop is ScanStop.BecauseTheAppIsStopping)
        {
            run.Fail(AppStoppingReason, at);

            return;
        }

        run.Cancel(CancelledReason, at);
    }

    /// <summary>
    /// A scan whose driver was replaced is interrupted, before it is stopped, before it fails, and it completes only
    /// when it walked through.
    /// </summary>
    public static void Settle(ScanRun run, ScanWalkEnd end, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(end);

        if (end.DriverReplaced)
        {
            run.Interrupt(at);
        }
        else if (end.Stopped is { } stop)
        {
            Stop(run, stop, at);
        }
        else if (end.Failure is { } reason)
        {
            run.Fail(reason, at);
        }
        else
        {
            run.Complete(at);
        }
    }

    public static void Abandon(ScanRun run, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.Fail(AbandonedReason, at);
    }
}
