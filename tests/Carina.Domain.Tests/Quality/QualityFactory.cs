using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

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

    public static QualityFacet Facet(
        int network = 32_736,
        int service = 1_024,
        string tuner = "adapter0",
        int hourOfDay = 21,
        TuneSystem kind = TuneSystem.IsdbT)
        => QualityFacet.Of(kind, new NetworkId(network), new ServiceId(service), new TunerDeviceId(tuner), hourOfDay);

    public static QualityObservation Measured(double observed, QualityFacet? facet = null, ThresholdBand? band = null)
        => QualityObservation.Of(facet ?? Facet(), ThresholdEvaluator.Judge(observed, band ?? PacketsLost()));

    public static QualityObservation Unmeasured(QualityFacet? facet = null, ThresholdBand? band = null)
        => QualityObservation.Of(facet ?? Facet(), ThresholdEvaluator.Judge(null, band ?? PacketsLost()));

    public static QualityObservation Unsupported(QualityFacet? facet = null)
        => QualityObservation.Unsupported(facet ?? Facet());

    public static QualityObservation Unreachable(QualityFacet? facet = null)
        => QualityObservation.Unreachable(facet ?? Facet());
}
