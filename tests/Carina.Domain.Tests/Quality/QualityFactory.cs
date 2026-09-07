using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

internal static class QualityFactory
{
    public static readonly DateTime Settled = new(2026, 8, 10, 3, 0, 0, DateTimeKind.Utc);

    public static Threshold Firm(double value, long observations = 741_375)
        => Threshold.Of(value, value, provisional: false, observations, Settled);

    public static Threshold Provisional(double value, long observations = 0)
        => Threshold.Provisionally(value, observations, Settled);

    public static Threshold Moved(double from, double to, bool provisional = true, long observations = 0)
        => Threshold.Of(from, to, provisional, observations, Settled);

    public static ThresholdBand PacketsLost(double warning = 0.0002, double unwatchable = 0.001)
        => ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            Provisional(warning),
            QualityThresholdKey.PacketsLostUnwatchable,
            Provisional(unwatchable));

    public static ThresholdBand LockRate(double warning = 0.9, double unwatchable = 0.5)
        => ThresholdBand.Of(
            ThresholdSense.Floor,
            QualityThresholdKey.LockRate,
            Provisional(warning),
            QualityThresholdKey.CarrierToNoiseFloor,
            Provisional(unwatchable));

    public static ThresholdBand WarningOnly(double warning = 0.0002)
        => ThresholdBand.Of(ThresholdSense.Ceiling, QualityThresholdKey.PacketsLostWarning, Provisional(warning));
}
