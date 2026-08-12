namespace Carina.Contracts;

/// <summary>
/// The driver's HTTP surface, served over the Unix socket only.
/// </summary>
/// <remarks>
/// The app may be newer than the driver it talks to, so a request for a path the
/// driver does not know answers 404 and the app degrades instead of failing. That
/// only holds while paths are added and never renamed.
/// </remarks>
public static class DriverEndpoints
{
    /// <summary>Liveness plus the hello exchange.</summary>
    public const string Health = "/health";

    /// <summary>State of every tuner device the driver owns.</summary>
    public const string Tuners = "/tuners";

    /// <summary>Sessions the driver currently holds; also the start endpoint.</summary>
    public const string Sessions = "/sessions";

    /// <summary>Reasons behind the diagnostic signals, most recent first.</summary>
    public const string Diagnostics = "/diagnostics";

    /// <summary>Server-sent signals about what the driver just did.</summary>
    public const string Events = "/events";

    /// <summary>The path of one session, for stopping it.</summary>
    public static string Session(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}";

    /// <summary>
    /// The transport stream of one session, chunked and unwrapped.
    /// </summary>
    /// <remarks>
    /// A failure after the first byte aborts the connection rather than closing it
    /// cleanly. The receiver treats any EOF as "incomplete, reconnect": a clean
    /// close would let a recording that stopped early read as one that finished.
    /// </remarks>
    public static string SessionStream(SessionId sessionId) =>
        $"{Sessions}/{Segment(sessionId)}/stream";

    /// <summary>
    /// The identifier as a path segment, refusing one this build cannot act on.
    /// </summary>
    /// <remarks>
    /// An unset identifier would interpolate to nothing and quietly address the
    /// collection instead of a member — a stop request that stops nothing, or a
    /// stream request that reads the list.
    /// </remarks>
    private static string Segment(SessionId sessionId) =>
        sessionId.Value
        ?? throw new ArgumentException(
            "The session id is unset; there is no path for it.",
            nameof(sessionId)
        );

    /// <summary>The fixed paths, for tests and diagnostics.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Health,
        Tuners,
        Sessions,
        Diagnostics,
        Events,
    ];
}
