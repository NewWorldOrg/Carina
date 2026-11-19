using System.Globalization;

using Carina.Domain.Channels;

namespace Carina.Domain.Quality;

public sealed record ThresholdBreach(
    QualityThresholdKey Breached,
    QualitySubject Subject,
    double Observed,
    Threshold Applied);

public sealed record ThresholdBreachWatchPlan(
    IReadOnlyList<ThresholdBreach> ToOpen,
    IReadOnlyList<QualityIncident> ToResolve);

public static class ThresholdBreachWatch
{
    public static readonly TimeSpan Looked = QualityPeriod.ShippedSpan;

    public static IReadOnlyList<ThresholdBreach> Recorded(IReadOnlyList<QualityLedgerRow> rows, QualityBands bands)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(bands);

        List<ThresholdBreach> found = [];

        foreach (QualityMetric metric in QualityMetrics.All)
        {
            ThresholdBand band = bands.For(metric);

            found.AddRange(rows
                .Select(row => (Row: row, Verdict: ThresholdEvaluator.Judge(QualitySurvey.Reading(row, metric), band)))
                .Where(read => read.Verdict.Breached is not null)
                .GroupBy(read => (Network: read.Row.Network.Value, Service: read.Row.Service.Value))
                .OrderBy(channel => channel.Key.Network)
                .ThenBy(channel => channel.Key.Service)
                .Select(channel => Worst(
                    Channel(channel.Key.Network, channel.Key.Service),
                    [.. channel.Select(read => read.Verdict)],
                    band.Sense)));
        }

        return found;
    }

    public static IReadOnlyList<ThresholdBreach> Received(
        IReadOnlyList<SignalFigures> figures,
        IReadOnlyList<QualityThresholdStanding> thresholds,
        IReadOnlySet<string> cannotLock)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(cannotLock);

        List<ThresholdBreach> found = [];

        foreach (QualityThresholdKey key in QualitySignalSurvey.Keys)
        {
            QualityThresholdStanding level = thresholds.FirstOrDefault(held => held.Key == key)
                                             ?? throw new ArgumentException(
                                                 $"A signal reading is held against the level kept under {key}, and none was handed over.",
                                                 nameof(thresholds));
            ThresholdBand band = ThresholdBand.Of(level.Shape.Sense, key, level.Setting);

            foreach (SignalFigures figure in figures.OrderBy(figure => figure.Tuner.Value, StringComparer.Ordinal))
            {
                if (figure.NothingWasTaken
                    || (key is QualityThresholdKey.LockRate && cannotLock.Contains(figure.Tuner.Value)))
                {
                    continue;
                }

                ThresholdVerdict verdict = ThresholdEvaluator.Judge(QualitySignalSurvey.Observed(key, figure), band);

                if (verdict.Breached is { } breached)
                {
                    found.Add(new ThresholdBreach(
                        breached,
                        QualitySubject.Of(QualitySubjectKind.Tuner, figure.Tuner.Value),
                        verdict.Observed!.Value,
                        verdict.Applied));
                }
            }
        }

        return found;
    }

    public static IReadOnlyList<ThresholdBreach> ReceivedByChannel(
        IReadOnlyList<ReceptionFigures> figures,
        IReadOnlyList<QualityThresholdStanding> thresholds,
        IReadOnlyList<ThresholdBreach> byTuner)
    {
        ArgumentNullException.ThrowIfNull(figures);
        ArgumentNullException.ThrowIfNull(thresholds);
        ArgumentNullException.ThrowIfNull(byTuner);

        List<ThresholdBreach> found = [];

        foreach (QualityThresholdKey key in QualitySignalSurvey.Keys.Where(key => key is not QualityThresholdKey.LockRate))
        {
            QualityThresholdStanding level = thresholds.FirstOrDefault(held => held.Key == key)
                                             ?? throw new ArgumentException(
                                                 $"A signal reading is held against the level kept under {key}, and none was handed over.",
                                                 nameof(thresholds));
            ThresholdBand band = ThresholdBand.Of(level.Shape.Sense, key, level.Setting);

            found.AddRange(figures
                .Where(reception => !reception.Figures.NothingWasTaken
                                    && !TunerNamed(byTuner, key, reception.Figures.Tuner.Value))
                .Select(reception => (reception.Subject, Verdict: ThresholdEvaluator.Judge(
                    QualitySignalSurvey.Observed(key, reception.Figures),
                    band)))
                .Where(read => read.Verdict.Breached is not null)
                .Select(read => new ThresholdBreach(
                    read.Verdict.Breached!.Value,
                    read.Subject,
                    read.Verdict.Observed!.Value,
                    read.Verdict.Applied)));
        }

        return found;
    }

    public static ThresholdBreachWatchPlan Plan(
        IReadOnlyList<ThresholdBreach> breaches,
        IReadOnlyList<QualityIncident> standing)
    {
        ArgumentNullException.ThrowIfNull(breaches);
        ArgumentNullException.ThrowIfNull(standing);

        List<QualityIncident> watched = [.. standing.Where(Watched)];

        return new ThresholdBreachWatchPlan(
            [.. breaches.Where(breach => !watched.Exists(incident => About(incident, breach)))],
            [.. watched.Where(incident => !breaches.Any(breach => About(incident, breach)))]);
    }

    public static QualityIncident Open(QualityIncidentId id, ThresholdBreach breach, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(breach);

        return QualityIncident.Detect(id, at, breach.Breached, breach.Subject, breach.Observed, breach.Applied);
    }

    public static IReadOnlySet<string> CannotLock(
        bool asked,
        IReadOnlyList<TunerTrouble> troubles,
        IReadOnlyList<QualityIncident> standing)
    {
        ArgumentNullException.ThrowIfNull(troubles);
        ArgumentNullException.ThrowIfNull(standing);

        return asked
            ? troubles
                .Where(trouble => trouble.Kind is TunerTroubleKind.NoLock)
                .Select(trouble => trouble.Tuner.Value)
                .ToHashSet(StringComparer.Ordinal)
            : TunerTroubleWatch.Troubled(standing)
                .Where(troubled => troubled.Value is TunerTroubleKind.NoLock)
                .Select(troubled => troubled.Key)
                .ToHashSet(StringComparer.Ordinal);
    }

    private static QualitySubject Channel(int network, int service)
        => QualitySubject.Of(
            QualitySubjectKind.Channel,
            string.Create(CultureInfo.InvariantCulture, $"{network}-{service}"));

    private static ThresholdBreach Worst(QualitySubject subject, IReadOnlyList<ThresholdVerdict> verdicts, ThresholdSense sense)
    {
        int worstFirst = sense is ThresholdSense.Ceiling ? -1 : 1;

        ThresholdVerdict worst = verdicts
            .OrderByDescending(verdict => verdict.Standing is QualityStanding.MayNotBeWatchable)
            .ThenBy(verdict => verdict.Observed!.Value * worstFirst)
            .First();

        return new ThresholdBreach(worst.Breached!.Value, subject, worst.Observed!.Value, worst.Applied);
    }

    private static bool TunerNamed(IReadOnlyList<ThresholdBreach> byTuner, QualityThresholdKey key, string tuner)
        => byTuner.Any(breach => breach.Breached == key
                                 && breach.Subject.Kind is QualitySubjectKind.Tuner
                                 && breach.Subject.Key == tuner);

    private static bool Watched(QualityIncident incident)
        => incident is { Owner: QualityIncidentOwner.Quality, HasSettled: false }
           && incident.Breached is not QualityThresholdKey.SupplySilence;

    private static bool About(QualityIncident incident, ThresholdBreach breach)
        => incident.Breached == breach.Breached && incident.Subject.Equals(breach.Subject);
}
