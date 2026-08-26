using Carina.Contracts;

namespace Carina.Driver.Ipc;

public static class DriverGreeting
{
    public static readonly IReadOnlyList<string> Capabilities =
    [
        DriverCapabilities.Recording,
        DriverCapabilities.Live,
        DriverCapabilities.QualityMetering,
        DriverCapabilities.DeviceDetection,
        DriverCapabilities.SessionStopReason,
        DriverCapabilities.TunerLedger,
        DriverCapabilities.LiveTunerToggle,
        DriverCapabilities.TypedTuning,
        DriverCapabilities.SignalQuality,
        DriverCapabilities.GracefulRestart,
        DriverCapabilities.RecordingExtension,
        DriverCapabilities.CcMeasurement,
        DriverCapabilities.ScrambleMeasurement,
        DriverCapabilities.DropPositions,
        DriverCapabilities.Storage,
        .. SignalQualityMetrics.All.Select(DriverCapabilities.SignalQualityMetric),
        .. SessionPurposes.Capabilities,
    ];

    public static DriverHello ForThisProcess() =>
        new(DriverProtocol.Version, Guid.NewGuid().ToString("N"), Capabilities);
}
