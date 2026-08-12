namespace Carina.Contracts;

/// <summary>
/// What the driver answers when the app first reaches it.
/// </summary>
/// <param name="ProtocolVersion">The version the driver was built against.</param>
/// <param name="InstanceId">Identifies this run of the driver process.</param>
/// <param name="Capabilities">What this build can do; see <see cref="DriverCapabilities"/>.</param>
/// <remarks>
/// The app reads <see cref="Capabilities"/> and not <see cref="ProtocolVersion"/> to
/// decide what to ask for. The version says which compatibility window the driver
/// belongs to; it does not say which features are present, because features are
/// added within a window.
///
/// <see cref="InstanceId"/> is the only thing that says the driver restarted. A
/// dropped connection says nothing — the app reconnects to the same process all the
/// time. A changed instance means every session the app remembered is gone, which
/// is what recording recovery and interrupt marking key off.
/// </remarks>
public sealed record DriverHello(
    int ProtocolVersion,
    string InstanceId,
    IReadOnlyList<string> Capabilities
)
{
    /// <summary>What this build can do. Never null, so a terse driver reads as "nothing extra".</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Capabilities ?? [];

    /// <summary>Whether the driver reported <paramref name="capability"/>.</summary>
    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="other"/> is a different run of the driver.</summary>
    public bool IsDifferentInstanceFrom(DriverHello? other) =>
        other is null || !string.Equals(InstanceId, other.InstanceId, StringComparison.Ordinal);
}
