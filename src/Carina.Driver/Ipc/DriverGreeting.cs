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
        DriverCapabilities.RecordingErasure,
        .. SignalQualityMetrics.All.Select(DriverCapabilities.SignalQualityMetric),
        .. SessionPurposes.Capabilities,
    ];

    public static IReadOnlyList<string> Unscrambling(bool descrambling) =>
        descrambling ? [.. Capabilities, DriverCapabilities.Descrambling] : Capabilities;

    public static DriverHello ForThisProcess(bool descrambling) =>
        new(
            DriverProtocol.Version,
            Guid.NewGuid().ToString("N"),
            Unscrambling(descrambling)
        );
}
