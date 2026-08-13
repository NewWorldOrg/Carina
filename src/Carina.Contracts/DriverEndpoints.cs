namespace Carina.Contracts;

public static class DriverEndpoints
{
    public const string Health = "/health";

    public const string Tuners = "/tuners";

    public const string Sessions = "/sessions";

    public const string Diagnostics = "/diagnostics";

    public const string Events = "/events";

    public const string SubscriberQuery = "as";

    public const string ViewerSubscriber = "viewer";

    public const string SurveySubscriber = "survey";

    public static string Session(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}";

    public static string SessionStream(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}/stream";

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
    ];
}
