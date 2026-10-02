using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

/// <summary>
/// What was read of one tuner's signal over a period.
/// </summary>
/// <param name="CarrierToNoiseUsual">
/// The lowest carrier to noise figure that more than half of what was read sits at or under, or null when none
/// was read.
/// </param>
/// <param name="BitErrorRateUsual">
/// The highest bit error rate that more than half of what was read sits at or over, or null when none was read.
/// </param>
public sealed record SignalFigures(
    TunerDeviceId Tuner,
    long Samples,
    long Locked,
    long Unmeasured,
    long Unreachable,
    int? CarrierToNoiseLowest,
    double? CarrierToNoiseUsual,
    double? BitErrorRateHighest,
    double? BitErrorRateUsual,
    IReadOnlyList<string> MetricsNotRead,
    DateTime? LastTakenAt)
{
    public long Taken => Samples - Unreachable;

    public bool NothingWasTaken => Samples > 0 && Taken is 0;

    public double? LockRate => Taken is 0 ? null : (double)Locked / Taken;
}

public sealed record QualitySignalRead(QualityThresholdKey Key, QualityReading Reading, DateTime? LastTakenAt);

public interface IQualitySignalReader
{
    Task<IReadOnlyList<SignalFigures>> FiguresAsync(QualityPeriod period, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(QualityTrendFrame frame, CancellationToken cancellationToken);

    Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(QualityPeriod period, CancellationToken cancellationToken);
}

public static class QualitySignalSurvey
{
    public static readonly IReadOnlyList<QualityThresholdKey> Keys =
    [
        QualityThresholdKey.LockRate,
        QualityThresholdKey.CarrierToNoiseFloor,
        QualityThresholdKey.BitErrorRateCeiling,
    ];

    public static IReadOnlyList<SignalFigures> Figures(
        IReadOnlyList<QualitySignalRollup> rolled,
        IReadOnlyList<QualitySignalSample> raw)
    {
        ArgumentNullException.ThrowIfNull(rolled);
        ArgumentNullException.ThrowIfNull(raw);

        return Figures([.. rolled.Select(QualitySignalWindow.Of), .. raw.Select(QualitySignalWindow.Of)]);
    }

    public static IReadOnlyList<SignalFigures> Figures(IReadOnlyList<QualitySignalWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        Dictionary<string, Gathering> byTuner = [];

        foreach (QualitySignalWindow window in windows)
        {
            Gathering held = Held(byTuner, window.Tuner);

            held.Samples += window.Samples;
            held.Locked += window.Locked;
            held.Unmeasured += window.Unmeasured;
            held.Unreachable += window.Unreachable;
            held.Coldest(window.CarrierToNoiseLowest);
            held.Worst(window.BitErrors.Count is 0 ? null : window.BitErrors.Max(peak => peak.Highest));
            held.Weighs(window);
            held.Names(window.MetricsNotRead);

            if (window.LastCarriedAt is { } carried)
            {
                held.Latest(carried);
            }
        }

        return
        [
            .. byTuner
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value.Done(new TunerDeviceId(pair.Key))),
        ];
    }

    /// <summary>
    /// Holds what each tuner usually read over the period against each level.
    /// </summary>
    public static IReadOnlyList<QualitySignalRead> Read(
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        IReadOnlyList<QualityThresholdStanding> thresholds)
        => Read(figures, subjects, thresholds, Observed);

    /// <summary>
    /// Holds the worst each tuner read over the period against each level.
    /// </summary>
    public static IReadOnlyList<QualitySignalRead> ReadAtWorst(
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        IReadOnlyList<QualityThresholdStanding> thresholds)
        => Read(figures, subjects, thresholds, Worst);

    private static IReadOnlyList<QualitySignalRead> Read(
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        IReadOnlyList<QualityThresholdStanding> thresholds,
        Func<QualityThresholdKey, SignalFigures, double?> reading)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(thresholds);

        return [.. Keys.Select(key => Read(key, figures, subjects, Level(key, thresholds), reading))];
    }

    private static QualitySignalRead Read(
        QualityThresholdKey key,
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        ThresholdBand band,
        Func<QualityThresholdKey, SignalFigures, double?> reading)
    {
        int measured = 0;
        int beyond = 0;
        int unsupported = 0;
        int unreachable = 0;
        DateTime? last = null;

        foreach (TunerDeviceId subject in subjects)
        {
            SignalFigures? figure = figures.FirstOrDefault(held => held.Tuner.Equals(subject));

            if (figure is null)
            {
                continue;
            }

            if (figure.NothingWasTaken)
            {
                unreachable++;

                continue;
            }

            if (reading(key, figure) is not { } observed)
            {
                unsupported += WasNotRead(key, figure) ? 1 : 0;

                continue;
            }

            measured++;

            if (QualityStandings.WentBeyond(ThresholdEvaluator.Judge(observed, band).Standing))
            {
                beyond++;
            }

            if (figure.LastTakenAt is { } taken && (last is null || taken > last))
            {
                last = taken;
            }
        }

        return new QualitySignalRead(
            key,
            QualityReading.Of(
                subjects.Count is 0 || unsupported < subjects.Count,
                unreachable is 0,
                subjects.Count,
                measured,
                beyond),
            last);
    }

    private static ThresholdBand Level(QualityThresholdKey key, IReadOnlyList<QualityThresholdStanding> thresholds)
    {
        QualityThresholdStanding standing = thresholds.FirstOrDefault(held => held.Key == key)
                                            ?? throw new ArgumentException(
                                                $"A signal reading is held against the level kept under {key}, and none was handed over.",
                                                nameof(thresholds));

        return ThresholdBand.Of(standing.Shape.Sense, key, standing.Setting);
    }

    private static bool WasNotRead(QualityThresholdKey key, SignalFigures figure) => key switch
    {
        QualityThresholdKey.CarrierToNoiseFloor =>
            figure.MetricsNotRead.Contains(SignalQualityMetrics.Cnr, StringComparer.Ordinal),
        QualityThresholdKey.BitErrorRateCeiling =>
            figure.MetricsNotRead.Contains(SignalQualityMetrics.PostViterbiBitError, StringComparer.Ordinal),
        _ => false,
    };

    /// <summary>
    /// What a tuner usually read over the period: the reading its standing is told by.
    /// </summary>
    public static double? Observed(QualityThresholdKey key, SignalFigures figure)
    {
        ArgumentNullException.ThrowIfNull(figure);

        return key switch
        {
            QualityThresholdKey.LockRate => figure.LockRate,
            QualityThresholdKey.CarrierToNoiseFloor => figure.CarrierToNoiseUsual,
            QualityThresholdKey.BitErrorRateCeiling => figure.BitErrorRateUsual,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "A signal reading is one of the ones this domain takes."),
        };
    }

    /// <summary>
    /// The worst a tuner read at any one moment of the period.
    /// </summary>
    public static double? Worst(QualityThresholdKey key, SignalFigures figure)
    {
        ArgumentNullException.ThrowIfNull(figure);

        return key switch
        {
            QualityThresholdKey.LockRate => figure.LockRate,
            QualityThresholdKey.CarrierToNoiseFloor => figure.CarrierToNoiseLowest,
            QualityThresholdKey.BitErrorRateCeiling => figure.BitErrorRateHighest,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "A signal reading is one of the ones this domain takes."),
        };
    }

    private static Gathering Held(Dictionary<string, Gathering> byTuner, TunerDeviceId tuner)
    {
        if (!byTuner.TryGetValue(tuner.Value, out Gathering? held))
        {
            held = new Gathering();
            byTuner[tuner.Value] = held;
        }

        return held;
    }

    private readonly record struct Weighed(double Reading, long Weight);

    private sealed class Gathering
    {
        private readonly SortedSet<string> notRead = new(StringComparer.Ordinal);

        private readonly List<Weighed> carrierToNoise = [];

        private readonly List<Weighed> bitErrorRates = [];

        private int? coldest;

        private double? worst;

        private DateTime? latest;

        public long Samples { get; set; }

        public long Locked { get; set; }

        public long Unmeasured { get; set; }

        public long Unreachable { get; set; }

        public void Coldest(int? reading)
        {
            if (reading is { } figure && (coldest is null || figure < coldest))
            {
                coldest = figure;
            }
        }

        public void Worst(double? reading)
        {
            if (reading is { } rate && (worst is null || rate > worst))
            {
                worst = rate;
            }
        }

        public void Weighs(QualitySignalWindow window)
        {
            long weight = Math.Max(1, window.Carried);

            if (window.CarrierToNoiseAverage is { } figure)
            {
                carrierToNoise.Add(new Weighed(figure, weight));
            }

            if (window.BitErrorRateAverage is { } rate)
            {
                bitErrorRates.Add(new Weighed(rate, weight));
            }
        }

        public void Names(IReadOnlyList<string> metrics)
        {
            foreach (string metric in metrics)
            {
                notRead.Add(metric);
            }
        }

        public void Latest(DateTime at)
        {
            if (latest is null || at > latest)
            {
                latest = at;
            }
        }

        public SignalFigures Done(TunerDeviceId tuner)
            => new(
                tuner,
                Samples,
                Locked,
                Unmeasured,
                Unreachable,
                coldest,
                Usual(carrierToNoise.OrderBy(weighed => weighed.Reading)),
                worst,
                Usual(bitErrorRates.OrderByDescending(weighed => weighed.Reading)),
                [.. notRead],
                latest);

        /// <summary>
        /// The first reading, counted from the worst, at which more than half of the weight has been passed.
        /// </summary>
        private static double? Usual(IOrderedEnumerable<Weighed> worstFirst)
        {
            Weighed[] ordered = [.. worstFirst];
            long whole = ordered.Sum(weighed => weighed.Weight);
            long passed = 0;

            foreach (Weighed weighed in ordered)
            {
                passed += weighed.Weight;

                if (passed * 2 > whole)
                {
                    return weighed.Reading;
                }
            }

            return null;
        }
    }
}
