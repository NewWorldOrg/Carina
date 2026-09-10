using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Events;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public sealed class QualityThresholdService(
    IQualityThresholdRepository thresholds,
    IQualityThresholdChangeRepository changes,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ServiceResult<IReadOnlyList<QualityThresholdBook>>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(cancellationToken);
        IReadOnlyList<QualityThresholdChange> history = await changes.ListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<QualityThresholdBook>>.Success(
        [
            .. standings.Select(standing => new QualityThresholdBook(
                standing,
                history.FirstOrDefault(change => change.Key == standing.Key))),
        ]);
    }

    public async Task<ServiceResult<QualityThresholdBook, QualityThresholdFailure>> ReviseAsync(
        QualityThresholdKey key,
        double value,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(cancellationToken);
        QualityThresholdStanding standing = standings.First(held => held.Key == key);

        if (!standing.Shape.Holds(value))
        {
            return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Failure(
                QualitySaying.OutOfRange(standing.Shape),
                QualityThresholdFailure.OutOfRange);
        }

        if (!QualityThresholdStanding.Ordered(key, value, standings))
        {
            return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Failure(
                QualitySaying.OutOfOrder(key),
                QualityThresholdFailure.OutOfOrder);
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;
        double previous = standing.Setting.Current;

        QualityThreshold revised = QualityThreshold.Rehydrate(
            key,
            Threshold.Of(standing.Setting.Default, value, standing.Setting.Provisional, standing.Setting.Observations, at),
            standing.UpdatedBy);

        await thresholds.SaveAsync(revised, cancellationToken);

        QualityThresholdChange recorded = QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            key,
            previous,
            value,
            at,
            null);

        await changes.AddAsync(recorded, cancellationToken);

        events.Signal(AppEventName.Quality);

        return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Success(new QualityThresholdBook(
            new QualityThresholdStanding(key, standing.Shape, revised.Setting, true, revised.UpdatedBy),
            recorded));
    }

    private async Task<IReadOnlyList<QualityThresholdStanding>> StandingsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Over(
            await thresholds.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime);
}
