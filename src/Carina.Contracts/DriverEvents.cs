namespace Carina.Contracts;

public static class DriverEvents
{
    public const string Tuners = "tuners";

    public const string Sessions = "sessions";

    public const string Draining = "draining";

    public const string Diagnostics = "diagnostics";

    public static readonly IReadOnlyList<string> All =
    [
        Tuners,
        Sessions,
        Draining,
        Diagnostics,
    ];

    public static bool IsKnown(string? name) =>
        name is not null && All.Contains(name, StringComparer.Ordinal);
}
