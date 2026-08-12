namespace Carina.Contracts;

/// <summary>
/// Names a driver may report in its hello.
/// </summary>
/// <remarks>
/// A capability is how the app finds out what the driver it happens to be paired
/// with can do. The list grows; a name, once shipped, keeps its meaning forever,
/// because an older app will still be asking for it.
/// </remarks>
public static class DriverCapabilities
{
    /// <summary>The driver writes recordings to its own output directory.</summary>
    public const string Recording = "recording";

    /// <summary>The driver serves a session's transport stream over the socket.</summary>
    public const string Live = "live";

    /// <summary>The driver counts continuity errors while a session runs.</summary>
    public const string QualityMetering = "qualityMetering";

    /// <summary>The driver descrambles in process.</summary>
    public const string Descrambling = "descrambling";
}
