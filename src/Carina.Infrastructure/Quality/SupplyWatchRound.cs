using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed record SupplyWatchPass(SupplyStanding Standing, int Opened, int Notified, int Resolved)
{
    public bool SaysAnything => Opened > 0 || Notified > 0 || Resolved > 0;
}

public sealed class SupplyWatchRound(
    IQualityThresholdRepository thresholds,
    IQualityIncidentRepository incidents,
    IQualitySupplyReader supply,
    IQualitySignalSampleRepository samples,
    IQualityLedgerReader ledger,
    IQualitySignalReader signals,
    IDriverClient driver,
    ISupplyStandingBoard board,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<SupplyWatchRound> logger)
{
    public async Task<SupplyWatchPass> WatchAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<QualityThresholdStanding> levels =
            QualityThresholdStanding.Over(await thresholds.ListAsync(cancellationToken), now);
        QualityThresholdStanding standing = levels.First(held => held.Key == QualityThresholdKey.SupplySilence);
        QualityThresholdStanding lockRate = levels.First(held => held.Key == QualityThresholdKey.LockRate);
        TimeSpan longest = TimeSpan.FromSeconds(standing.Setting.Current);

        List<SupplyReading> readings = [];

        readings.AddRange(await supply.ReadAsync(cancellationToken));

        (bool asked, IReadOnlyList<SupplyReading> tuners, IReadOnlyList<TunerTrouble> troubles) =
            await TunersAsync(now, cancellationToken);

        readings.AddRange(tuners);

        IReadOnlyList<SupplySilenceFinding> quiet = SupplyWatch.Quiet(readings, longest, now);
        IReadOnlyList<QualityIncident> unsettled = await incidents.ListUnsettledAsync(cancellationToken);
        SupplyWatchPlan plan = SupplyWatch.Plan(quiet, unsettled, Observed(asked));
        TunerTroubleWatchPlan troubled = asked
            ? TunerTroubleWatch.Plan(troubles, unsettled)
            : new TunerTroubleWatchPlan([], []);

        ThresholdBreachWatchPlan breached = ThresholdBreachWatch.Plan(
            await BreachesAsync(levels, ThresholdBreachWatch.CannotLock(asked, troubles, unsettled), now, cancellationToken),
            unsettled);

        int opened = await OpenAsync(plan.ToOpen, standing.Setting, now, cancellationToken)
            + await RestateAsync(troubled.ToOpen, lockRate.Setting, now, cancellationToken)
            + await OpenAsync(breached.ToOpen, now, cancellationToken);
        int resolved = await ResolveAsync(
            [.. plan.ToResolve, .. troubled.ToResolve, .. breached.ToResolve],
            now,
            cancellationToken);
        int notified = await NotifyAsync(now, cancellationToken);

        if (notified > 0 || resolved > 0)
        {
            events.Signal(AppEventName.Quality);
        }

        var pass = new SupplyWatchPass(
            SupplyStanding.Of(now, standing.Setting, asked, Supplies(readings, quiet)),
            opened,
            notified,
            resolved);

        board.Held(pass.Standing);
        Report(pass);

        return pass;
    }

    private static IReadOnlySet<SupplySilence> Observed(bool asked)
        => asked ? SupplySilences.Every : SupplySilences.TheLedgerAnswersFor;

    private static IReadOnlyList<SupplySilenceStanding> Supplies(
        IReadOnlyList<SupplyReading> readings,
        IReadOnlyList<SupplySilenceFinding> quiet)
        =>
        [
            .. Enum.GetValues<SupplySilence>().Select(silence => new SupplySilenceStanding(
                silence,
                readings.Count(reading => reading.Silence == silence),
                quiet.Count(finding => finding.Silence == silence))),
        ];

    private async Task<TunerReadings> TunersAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<TunerSnapshot>> asked = await driver.GetTunersAsync(cancellationToken);

        if (!asked.TryGetValue(out IReadOnlyList<TunerSnapshot>? tuners))
        {
            logger.LogWarning(
                "The driver would not say what tuners it holds, so this pass leaves every signal sample silence "
                + "where the last pass that could see them left it.");

            return new TunerReadings(false, [], []);
        }

        IReadOnlyDictionary<TunerDeviceId, DateTime> latest =
            await samples.ListLastTakenAsync(cancellationToken);
        List<SupplyReading> read = [];

        foreach (TunerSnapshot tuner in tuners)
        {
            if (tuner.State is TunerState.Disabled
                || string.IsNullOrWhiteSpace(tuner.DeviceId)
                || tuner.CurrentSession is not { } session
                || session.SessionId.IsUnset
                || session.StartedAt is not { } opened)
            {
                continue;
            }

            DateTime since = opened.UtcDateTime > now ? now : opened.UtcDateTime;
            var device = new TunerDeviceId(tuner.DeviceId);

            if (latest.TryGetValue(device, out DateTime taken) && taken > since)
            {
                since = taken;
            }

            read.Add(SupplyReading.Of(
                SupplySilence.SignalSamples,
                QualitySubject.Of(QualitySubjectKind.Tuner, device.Value),
                since));
        }

        return new TunerReadings(true, read, TunerTroubles.Of(tuners));
    }

    private async Task<int> OpenAsync(
        IReadOnlyList<SupplySilenceFinding> opening,
        Threshold applied,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (SupplySilenceFinding finding in opening)
        {
            await incidents.AddAsync(
                QualityIncident.Detect(
                    QualityIncidentId.New(),
                    now,
                    QualityThresholdKey.SupplySilence,
                    finding.Subject,
                    finding.Seconds,
                    HeldAgainst(finding, applied, now),
                    silence: finding.Silence),
                cancellationToken);
        }

        return opening.Count;
    }

    private static Threshold HeldAgainst(SupplySilenceFinding finding, Threshold shared, DateTime now)
        => finding.Allowed is { } allowed
            ? Threshold.Provisionally(allowed.TotalSeconds, 0, now)
            : shared;

    private async Task<IReadOnlyList<ThresholdBreach>> BreachesAsync(
        IReadOnlyList<QualityThresholdStanding> levels,
        IReadOnlySet<string> cannotLock,
        DateTime now,
        CancellationToken cancellationToken)
    {
        QualityPeriod looked = QualityPeriod.Of(now - ThresholdBreachWatch.Looked, now, now)
                               ?? throw new InvalidOperationException(
                                   "The span a breach is looked for over is one a period can be read across.");

        IReadOnlyList<ThresholdBreach> byTuner = ThresholdBreachWatch.Received(
            await signals.FiguresAsync(looked, cancellationToken),
            levels,
            cannotLock);

        return
        [
            .. ThresholdBreachWatch.Recorded(
                await ledger.ReadAsync(looked, cancellationToken),
                QualityThresholdStanding.Bands(levels)),
            .. byTuner,
            .. ThresholdBreachWatch.ReceivedByChannel(
                await signals.ReceptionsAsync(looked, cancellationToken),
                levels,
                byTuner),
        ];
    }

    private async Task<int> OpenAsync(
        IReadOnlyList<ThresholdBreach> opening,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (ThresholdBreach breach in opening)
        {
            await incidents.AddAsync(
                ThresholdBreachWatch.Open(QualityIncidentId.New(), breach, now),
                cancellationToken);
        }

        return opening.Count;
    }

    private async Task<int> RestateAsync(
        IReadOnlyList<TunerTrouble> troubles,
        Threshold applied,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (TunerTrouble trouble in troubles)
        {
            await incidents.AddAsync(
                TunerTroubleWatch.Restate(QualityIncidentId.New(), trouble, now, applied),
                cancellationToken);
        }

        return troubles.Count;
    }

    private async Task<int> ResolveAsync(
        IReadOnlyList<QualityIncident> resolving,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (QualityIncident incident in resolving)
        {
            incident.Resolve(now);

            await incidents.SaveAsync(incident, cancellationToken);
        }

        return resolving.Count;
    }

    private async Task<int> NotifyAsync(DateTime now, CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityIncident> unsettled = await incidents.ListUnsettledAsync(cancellationToken);
        int notified = 0;

        foreach (QualityIncident incident in unsettled)
        {
            if (incident.State is not QualityIncidentState.Detected)
            {
                continue;
            }

            incident.Notify(now);

            await incidents.SaveAsync(incident, cancellationToken);

            notified++;
        }

        return notified;
    }

    private void Report(SupplyWatchPass pass)
    {
        if (!pass.SaysAnything)
        {
            return;
        }

        logger.LogWarning(
            "A supply watch held {Seconds}s of quiet against {Watched} supply reading(s): {Opened} went quiet, "
            + "were said by the driver to be in trouble or read beyond a level, {Notified} were told about, and "
            + "{Resolved} cleared.",
            pass.Standing.Applied.Current,
            pass.Standing.Supplies.Sum(supply => supply.Watched),
            pass.Opened,
            pass.Notified,
            pass.Resolved);
    }

    private sealed record TunerReadings(
        bool Asked,
        IReadOnlyList<SupplyReading> Readings,
        IReadOnlyList<TunerTrouble> Troubles);
}
