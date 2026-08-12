namespace Carina.Contracts;

/// <summary>
/// What the driver answers when the app first reaches it.
/// </summary>
/// <param name="ProtocolVersion">The version the driver was built against.</param>
/// <param name="Capabilities">What this build can do; see <see cref="DriverCapabilities"/>.</param>
/// <remarks>
/// The app reads <see cref="Capabilities"/> and not <see cref="ProtocolVersion"/> to
/// decide what to ask for. The version says which compatibility window the driver
/// belongs to; it does not say which features are present, because features are
/// added within a window.
/// </remarks>
public sealed record DriverHello(int ProtocolVersion, IReadOnlyList<string> Capabilities)
{
    /// <summary>What this build can do. Never null, so a terse driver reads as "nothing extra".</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Capabilities ?? [];

    /// <summary>Whether the driver reported <paramref name="capability"/>.</summary>
    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);
}
