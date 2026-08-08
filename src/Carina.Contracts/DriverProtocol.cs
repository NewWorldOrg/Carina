namespace Carina.Contracts;

/// <summary>
/// Identity of the driver/app IPC protocol.
/// </summary>
/// <remarks>
/// The two processes are released on independent tags, so "old driver + new app"
/// is the normal state. Contract changes are therefore additive only, and the app
/// feature-detects through capabilities rather than through this number. The
/// version is bumped only when a deliberate compatibility window is opened.
/// </remarks>
public static class DriverProtocol
{
    /// <summary>Current protocol version.</summary>
    public const int Version = 1;
}
