using System.Reflection;

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
        DriverCapabilities.StrayFileErasure,
        DriverCapabilities.LnbPowerSwitch,
        DriverCapabilities.CardIdentification,
        DriverCapabilities.DeviceHardware,
        DriverCapabilities.CardReaderStatus,
        .. SignalQualityMetrics.All.Select(DriverCapabilities.SignalQualityMetric),
        .. SessionPurposes.Capabilities,
    ];

    public static IReadOnlyList<string> Unscrambling(bool descrambling) =>
        descrambling ? [.. Capabilities, DriverCapabilities.Descrambling] : Capabilities;

    /// <summary>
    /// The version this driver was built as, without the source revision the build appends.
    /// </summary>
    public static readonly string Version =
        (typeof(DriverGreeting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty).Split('+')[0].Trim();

    public static DriverHello ForThisProcess(bool descrambling, string? name = null, DateTimeOffset? startedAt = null) =>
        new(
            DriverProtocol.Version,
            Guid.NewGuid().ToString("N"),
            Unscrambling(descrambling)
        )
        {
            Version = Version.Length is 0 ? null : Version,
            StartedAt = startedAt,
            Name = name,
        };
}
