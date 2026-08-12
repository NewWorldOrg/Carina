namespace Carina.Contracts;

/// <summary>
/// Names carried by the driver's server-sent event stream.
/// </summary>
/// <remarks>
/// Same rule as the app's stream: a signal says what moved, the app re-reads the
/// state over the driver's REST endpoints. The exception is the shutdown notice,
/// which exists so the app can stop asking for new sessions before the socket goes
/// away; it still carries nothing.
/// </remarks>
public static class DriverEvents
{
    /// <summary>A tuner device changed state — taken, released or faulted.</summary>
    public const string Tuners = "tuners";

    /// <summary>A session started, stopped or failed.</summary>
    public const string Sessions = "sessions";

    /// <summary>The driver stopped accepting new sessions and is draining.</summary>
    public const string Draining = "draining";

    /// <summary>Something went wrong that the app should surface — no space, for one.</summary>
    public const string Diagnostics = "diagnostics";

    /// <summary>Every name the driver's stream may carry.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Tuners,
        Sessions,
        Draining,
        Diagnostics,
    ];

    /// <summary>Whether <paramref name="name"/> belongs to the agreed set.</summary>
    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}
