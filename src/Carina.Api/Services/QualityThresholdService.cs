using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Quality;

namespace Carina.Api.Services;

public sealed class QualityThresholdService(
    IQualityThresholdRepository thresholds,
    IQualityThresholdChangeRepository changes,
    IAtomicWrite writes,
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

        return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Success(await KeepAsync(
            standing,
            QualityThresholdSettling.ByHand(standing, value, clock.GetUtcNow().UtcDateTime),
            cancellationToken));
    }

    public async Task<ServiceResult<QualityThresholdBook>> ReleaseAsync(
        QualityThresholdKey key,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(cancellationToken);
        QualityThresholdStanding standing = standings.First(held => held.Key == key);

        return ServiceResult<QualityThresholdBook>.Success(await KeepAsync(
            standing,
            QualityThresholdSettling.Released(standing, clock.GetUtcNow().UtcDateTime),
            cancellationToken));
    }

    private async Task<QualityThresholdBook> KeepAsync(
        QualityThresholdStanding standing,
        QualityThresholdSettled settled,
        CancellationToken cancellationToken)
    {
        if (settled.Change is not { } recorded)
        {
            return new QualityThresholdBook(
                standing,
                (await changes.ListAsync(cancellationToken)).FirstOrDefault(change => change.Key == standing.Key));
        }

        await writes.AllOrNothingAsync(
            async token =>
            {
                await thresholds.SaveAsync(settled.Threshold, token);
                await changes.AddAsync(recorded, token);

                return recorded;
            },
            cancellationToken);

        events.Signal(AppEventName.Quality);

        return new QualityThresholdBook(
            standing with
            {
                Setting = settled.Threshold.Setting,
                Stored = true,
                ByHand = settled.Threshold.ByHand,
                Measurement = settled.Threshold.Measurement,
            },
            recorded);
    }

    private async Task<IReadOnlyList<QualityThresholdStanding>> StandingsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Over(
            await thresholds.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime);
}
