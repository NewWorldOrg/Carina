using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Quality;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed record QualityThresholdMeasurePass(int Measured, int Moved);

public sealed class QualityThresholdMeasureRound(
    IQualityThresholdRepository thresholds,
    IQualityThresholdChangeRepository changes,
    IQualitySessionMeasurementRepository sessions,
    IQualitySignalSampleRepository samples,
    IAtomicWrite writes,
    IAppEventPublisher events,
    QualitySignalSettings settings,
    TimeProvider clock,
    ILogger<QualityThresholdMeasureRound> logger)
{
    public async Task<QualityThresholdMeasurePass> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime from = now - settings.KeepSamplesFor;
        double droppedFrom = QualityThresholdStanding.Over(await thresholds.ListAsync(cancellationToken), now)
            .First(standing => standing.Key == QualityThresholdKey.PacketsLostWarning)
            .Setting.Current;
        IReadOnlyList<SessionSignal> read = SignalThresholdMeasure.Read(
            await sessions.ListStartedBetweenAsync(from, now, cancellationToken),
            await samples.ListTakenBetweenAsync(from, now, cancellationToken));
        int measured = 0;
        int moved = 0;

        foreach (QualityThresholdKey key in SignalThresholdMeasure.Keys)
        {
            if (SignalThresholdMeasure.Measure(key, read, droppedFrom, now) is not { } measurement)
            {
                continue;
            }

            QualityThresholdSettled settled = await KeepAsync(key, measurement, cancellationToken);

            measured++;
            moved += settled.Change is null ? 0 : 1;
        }

        if (measured > 0)
        {
            events.Signal(AppEventName.Quality);
        }

        logger.LogInformation(
            "Measured {Measured} signal level(s) from {Sessions} session(s) since {From:o}; {Moved} moved.",
            measured,
            read.Count,
            from,
            moved);

        return new QualityThresholdMeasurePass(measured, moved);
    }

    private async Task<QualityThresholdSettled> KeepAsync(
        QualityThresholdKey key,
        QualityThresholdMeasurement measurement,
        CancellationToken cancellationToken)
        => await writes.AllOrNothingAsync(
            async token =>
            {
                await thresholds.TakeTurnAsync(token);

                QualityThresholdSettled settled = QualityThresholdSettling.Measured(
                    QualityThresholdStanding.Over(await thresholds.ListAsync(token), measurement.MeasuredAt)
                        .First(standing => standing.Key == key),
                    measurement);

                await thresholds.SaveAsync(settled.Threshold, token);

                if (settled.Change is { } change)
                {
                    await changes.AddAsync(change, token);
                }

                return settled;
            },
            cancellationToken);
}
