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

    public const string SignalQualityMetricPrefix = "signalQuality.";

    public static string SignalQualityMetric(string metric) =>
        SignalQualityMetricPrefix + metric;

    public static string? MetricIn(string capability) =>
        capability.StartsWith(SignalQualityMetricPrefix, StringComparison.Ordinal)
        && capability.Length > SignalQualityMetricPrefix.Length
            ? capability[SignalQualityMetricPrefix.Length..]
            : null;
}
