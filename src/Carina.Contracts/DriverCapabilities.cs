namespace Carina.Contracts;

public static class DriverCapabilities
{
    public const string Recording = "recording";

    public const string Live = "live";

    public const string QualityMetering = "qualityMetering";

    public const string Descrambling = "descrambling";

    public const string SignalQuality = "signalQuality";

    public const string SessionStopReason = "sessionStopReason";

    public const string LiveTunerToggle = "liveTunerToggle";

    public const string TypedTuning = "typedTuning";

    public const string DeviceDetection = "deviceDetection";

    public const string TunerLedger = "tunerLedger";

    public const string GracefulRestart = "gracefulRestart";

    public const string CcMeasurement = "ccMeasurement";

    public const string ScrambleMeasurement = "scrambleMeasurement";

    public const string DropPositions = "dropPositions";

    public const string RecordingExtension = "recordingExtension";

    public const string Storage = "storage";

    public const string SignalQualityMetricPrefix = "signalQuality.";

    public const string SessionPurposePrefix = "sessionPurpose.";

    public static string SignalQualityMetric(string metric) =>
        SignalQualityMetricPrefix + metric;

    public static string? MetricIn(string capability) =>
        capability.StartsWith(SignalQualityMetricPrefix, StringComparison.Ordinal)
        && capability.Length > SignalQualityMetricPrefix.Length
            ? capability[SignalQualityMetricPrefix.Length..]
            : null;

    public static string Purpose(string purpose) => SessionPurposePrefix + purpose;

    public static string? PurposeIn(string capability) =>
        capability.StartsWith(SessionPurposePrefix, StringComparison.Ordinal)
        && capability.Length > SessionPurposePrefix.Length
            ? capability[SessionPurposePrefix.Length..]
            : null;
}
