namespace Carina.Domain.Scans;

public enum ScanStop
{
    AsRequested = 1,

    BecauseTheAppIsStopping = 2,
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

    public static void Abandon(ScanRun run, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.Fail(AbandonedReason, at);
    }
}
