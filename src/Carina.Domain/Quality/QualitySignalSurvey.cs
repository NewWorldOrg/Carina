using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed record SignalFigures(
    TunerDeviceId Tuner,
    long Samples,
    long Locked,
    long Unmeasured,
    long Unreachable,
    int? CarrierToNoiseLowest,
    double? BitErrorRateHighest,
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

        Dictionary<string, Gathering> byTuner = [];

        foreach (QualitySignalRollup rollup in rolled)
        {
            Gathering held = Held(byTuner, rollup.Tuner);

            held.Samples += rollup.Samples;
            held.Locked += rollup.Locked;
            held.Unmeasured += rollup.Unmeasured;
            held.Unreachable += rollup.Unreachable;
            held.Coldest(rollup.CarrierToNoiseLowest);
            held.Worst(rollup.BitErrors.Count is 0 ? null : rollup.BitErrors.Max(rate => rate.Highest));

            if (rollup.CarrierToNoiseLowest is not null || rollup.BitErrors.Count > 0)
            {
                held.Latest(rollup.WindowStart);
            }
        }

        foreach (QualitySignalSample sample in raw)
        {
            Gathering held = Held(byTuner, sample.Tuner);

            held.Samples++;
            held.Locked += sample.Signal.Locked ? 1 : 0;
            held.Unreachable += sample.Signal.WasTaken ? 0 : 1;
            held.Unmeasured += sample.Signal.WasTaken && !sample.Signal.CarriesAnyValue ? 1 : 0;
            held.Coldest(sample.Signal.CarrierToNoiseMilliDecibels);
            held.Worst(Highest(sample.Signal.BitErrors));
            held.Names(sample.Signal.MetricsNotRead);

            if (sample.Signal.CarriesAnyValue)
            {
                held.Latest(sample.TakenAt);
            }
        }

        return
        [
            .. byTuner
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value.Done(new TunerDeviceId(pair.Key))),
        ];
    }

    public static IReadOnlyList<QualitySignalRead> Read(
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        IReadOnlyList<QualityThresholdStanding> thresholds)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(thresholds);

        return [.. Keys.Select(key => Read(key, figures, subjects, Level(key, thresholds)))];
    }

    private static QualitySignalRead Read(
        QualityThresholdKey key,
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<TunerDeviceId> subjects,
        ThresholdBand band)
    {
        int measured = 0;
        int beyond = 0;
        int unsupported = 0;
        int unreachable = 0;
        DateTime? last = null;

        foreach (TunerDeviceId subject in subjects)
        {
            SignalFigures? figure = figures.FirstOrDefault(
                held => string.Equals(held.Tuner.Value, subject.Value, StringComparison.Ordinal));

            if (figure is null)
            {
                continue;
            }

            if (figure.NothingWasTaken)
            {
                unreachable++;

                continue;
            }

            if (Observed(key, figure) is not { } observed)
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

    private static double? Observed(QualityThresholdKey key, SignalFigures figure) => key switch
    {
        QualityThresholdKey.LockRate => figure.LockRate,
        QualityThresholdKey.CarrierToNoiseFloor => figure.CarrierToNoiseLowest,
        QualityThresholdKey.BitErrorRateCeiling => figure.BitErrorRateHighest,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "A signal reading is one of the ones this domain takes."),
    };

    private static double? Highest(IReadOnlyList<LayerBitErrorCounts> counts)
    {
        double? highest = null;

        foreach (LayerBitErrorCounts layer in counts)
        {
            if (layer.ErrorRate is { } rate && (highest is null || rate > highest))
            {
                highest = rate;
            }
        }

        return highest;
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

    private sealed class Gathering
    {
        private readonly SortedSet<string> notRead = new(StringComparer.Ordinal);

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
            => new(tuner, Samples, Locked, Unmeasured, Unreachable, coldest, worst, [.. notRead], latest);
    }
}
