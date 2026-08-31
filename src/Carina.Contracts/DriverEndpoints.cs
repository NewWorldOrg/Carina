namespace Carina.Contracts;

public static class DriverEndpoints
{
    public const string Health = "/health";

    public const string Tuners = "/tuners";

    public const string Sessions = "/sessions";

    public const string Diagnostics = "/diagnostics";

    public const string Events = "/events";

    public const string DevicesDetected = "/devices/detected";

    public const string TunerLedger = "/tuners/ledger";

    public const string Restart = "/restart";

    public const string Storage = "/storage";

    public const string Recordings = "/recordings";

    public const string SubscriberQuery = "as";

    public const string OutputRootQuery = "root";

    public const string ViewerSubscriber = "viewer";

    public const string SurveySubscriber = "survey";

    public const string PiggybackSubscriber = "piggyback";

    public static string Session(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}";

    public static string SessionStream(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}/stream";

    public static string Tuner(string deviceId) =>
        WireName.IsUsable(deviceId)
            ? $"{Tuners}/{deviceId}"
            : throw new ArgumentException(
                $"A device id is {WireName.Description}; there is no path for '{deviceId}'.",
                nameof(deviceId)
            );

    public static string Recording(string recordingId) =>
        WireName.IsUsable(recordingId)
            ? $"{Recordings}/{recordingId}"
            : throw new ArgumentException(
                $"A recording id is {WireName.Description}; there is no path for '{recordingId}'.",
                nameof(recordingId)
            );

    private static string Segment(SessionId sessionId) =>
        sessionId.Value
        ?? throw new ArgumentException(
            "The session id is unset; there is no path for it.",
            nameof(sessionId)
        );

    public static readonly IReadOnlyList<string> All =
    [
        Health,
        Tuners,
        Sessions,
        Diagnostics,
        Events,
        DevicesDetected,
        TunerLedger,
        Restart,
        Storage,
        Recordings,
    ];
}
