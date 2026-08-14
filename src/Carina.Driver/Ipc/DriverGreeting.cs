using Carina.Contracts;

namespace Carina.Driver.Ipc;

public static class DriverGreeting
{
    public static readonly IReadOnlyList<string> Capabilities =
    [
        DriverCapabilities.Recording,
        DriverCapabilities.Live,
        DriverCapabilities.QualityMetering,
        DriverCapabilities.LiveTunerToggle,
        DriverCapabilities.TypedTuning,
        DriverCapabilities.SignalQuality,
        .. SignalQualityMetrics.All.Select(DriverCapabilities.SignalQualityMetric),
    ];

    public static DriverHello ForThisProcess() =>
        new(DriverProtocol.Version, Guid.NewGuid().ToString("N"), Capabilities);
}
