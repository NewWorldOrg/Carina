namespace Carina.Contracts;

public static class DriverEvents
{
    public const string Tuners = "tuners";

    public const string Sessions = "sessions";

    public const string Draining = "draining";

    public const string Diagnostics = "diagnostics";

    public const string SessionTuned = "sessionTuned";

    public const string SessionLockLost = "sessionLockLost";

    public const string TunerHealthChanged = "tunerHealthChanged";

    public const string SessionStopRequested = "sessionStopRequested";

    public const string RecordingProgress = "recordingProgress";

    public static readonly IReadOnlyList<string> All =
    [
        Tuners,
        Sessions,
        Draining,
        Diagnostics,
        SessionTuned,
        SessionLockLost,
        TunerHealthChanged,
        SessionStopRequested,
        RecordingProgress,
    ];

    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}
