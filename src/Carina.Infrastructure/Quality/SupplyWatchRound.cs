using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed record SupplyWatchTally(SupplySilence Silence, int Watched, int Quiet);

public sealed record SupplyWatchPass(
    DateTime At,
    Threshold Applied,
    IReadOnlyList<SupplyWatchTally> Tallies,
    bool TunersWereAsked,
    int Opened,
    int Notified,
    int Resolved)
{
    public bool SaysAnything => Opened > 0 || Notified > 0 || Resolved > 0;
}

public sealed class SupplyWatchRound(
    IQualityThresholdRepository thresholds,
    IQualityIncidentRepository incidents,
    IQualitySupplyReader supply,
    IQualitySignalSampleRepository samples,
    IDriverClient driver,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<SupplyWatchRound> logger)
{
    public async Task<SupplyWatchPass> WatchAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        QualityThresholdStanding standing = QualityThresholdStanding
            .Over(await thresholds.ListAsync(cancellationToken), now)
            .First(held => held.Key == QualityThresholdKey.SupplySilence);
        TimeSpan longest = TimeSpan.FromSeconds(standing.Setting.Current);

        List<SupplyReading> readings = [];

        readings.AddRange(await supply.ReadAsync(cancellationToken));

        (bool asked, IReadOnlyList<SupplyReading> tuners) = await TunersAsync(now, cancellationToken);

        readings.AddRange(tuners);

        IReadOnlyList<SupplySilenceFinding> quiet = SupplyWatch.Quiet(readings, longest, now);
        IReadOnlyList<QualityIncident> unsettled = await incidents.ListUnsettledAsync(cancellationToken);
        SupplyWatchPlan plan = SupplyWatch.Plan(quiet, unsettled, Observed(asked));

        int opened = await OpenAsync(plan.ToOpen, standing.Setting, now, cancellationToken);
        int resolved = await ResolveAsync(plan.ToResolve, now, cancellationToken);
        int notified = await NotifyAsync(now, cancellationToken);

        if (notified > 0 || resolved > 0)
        {
            events.Signal(AppEventName.Quality);
        }

        var pass = new SupplyWatchPass(
            now,
            standing.Setting,
            Tallies(readings, quiet),
            asked,
            opened,
            notified,
            resolved);

        Report(pass);

        return pass;
    }

    private static IReadOnlySet<SupplySilence> Observed(bool asked)
        => asked ? SupplySilences.Every : SupplySilences.TheLedgerAnswersFor;

    private static IReadOnlyList<SupplyWatchTally> Tallies(
        IReadOnlyList<SupplyReading> readings,
        IReadOnlyList<SupplySilenceFinding> quiet)
        =>
        [
            .. Enum.GetValues<SupplySilence>().Select(silence => new SupplyWatchTally(
                silence,
                readings.Count(reading => reading.Silence == silence),
                quiet.Count(finding => finding.Silence == silence))),
        ];

    private async Task<(bool Asked, IReadOnlyList<SupplyReading> Readings)> TunersAsync(
        DateTime now,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<TunerSnapshot>> asked = await driver.GetTunersAsync(cancellationToken);

        if (!asked.TryGetValue(out IReadOnlyList<TunerSnapshot>? tuners))
        {
            logger.LogWarning(
                "The driver would not say what tuners it holds, so this pass leaves every signal sample silence "
                + "where the last pass that could see them left it.");

            return (false, []);
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

        return (true, read);
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
                    applied,
                    silence: finding.Silence),
                cancellationToken);
        }

        return opening.Count;
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
            + "{Notified} were told about, and {Resolved} started being heard from again.",
            pass.Applied.Current,
            pass.Tallies.Sum(tally => tally.Watched),
            pass.Opened,
            pass.Notified,
            pass.Resolved);
    }
}
