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
    string? InstanceId,
    IReadOnlyList<string> Capabilities
)
{
    /// <summary>
    /// Identifies this run of the driver process, when the driver names one.
    /// </summary>
    /// <remarks>
    /// A driver built before the field existed sends nothing, which is not the same
    /// as sending the same value as last time. It reads as unknown so that the app
    /// takes the restart path rather than assuming its sessions survived.
    /// </remarks>
    public string? InstanceId { get; init; } =
        string.IsNullOrEmpty(InstanceId) ? null : InstanceId;

    /// <summary>What this build can do. Never null, so a terse driver reads as "nothing extra".</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Capabilities ?? [];

    /// <summary>Whether the driver reported <paramref name="capability"/>.</summary>
    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>
    /// Whether this is a different run of the driver from <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Unknown counts as different. Answering "same" for a driver that does not name
    /// its instance would leave the app believing sessions it can no longer see are
    /// still running, which is the one mistake this question exists to prevent.
    /// </remarks>
    public bool IsDifferentInstanceFrom(DriverHello? other) =>
        InstanceId is null
        || other?.InstanceId is null
        || !string.Equals(InstanceId, other.InstanceId, StringComparison.Ordinal);
}
