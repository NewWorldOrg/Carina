namespace Carina.Contracts;

public sealed record DriverHello(
    int ProtocolVersion,
    string? InstanceId,
    IReadOnlyList<string> Capabilities
)
{
    public string? InstanceId { get; init; } =
        string.IsNullOrEmpty(InstanceId) ? null : InstanceId;

    public IReadOnlyList<string> Capabilities { get; init; } = Capabilities ?? [];

    public bool Draining { get; init; }

    public bool Supports(string capability) =>
        Capabilities.Contains(capability, StringComparer.Ordinal);

    public bool SupportsSignalQualityMetric(string metric) =>
        Supports(DriverCapabilities.SignalQualityMetric(metric));

    public IReadOnlyList<string> DeclaredSignalQualityMetrics() =>
        [.. Capabilities.Select(DriverCapabilities.MetricIn).OfType<string>()];

    public bool IsDifferentInstanceFrom(DriverHello? other) =>
        InstanceId is null
        || other?.InstanceId is null
        || !string.Equals(InstanceId, other.InstanceId, StringComparison.Ordinal);
}
