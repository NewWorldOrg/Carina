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
        string? by,
        CancellationToken cancellationToken)
        => await SettleAsync(
            key,
            (standing, standings, at) =>
            {
                if (!standing.Shape.Holds(value))
                {
                    return Refused(QualitySaying.OutOfRange(standing.Shape), QualityThresholdFailure.OutOfRange);
                }

                return QualityThresholdStanding.Ordered(key, value, standings)
                    ? Settling.To(QualityThresholdSettling.ByHand(standing, value, at, by))
                    : Refused(QualitySaying.OutOfOrder(key), QualityThresholdFailure.OutOfOrder);
            },
            cancellationToken);

    public async Task<ServiceResult<QualityThresholdBook, QualityThresholdFailure>> ReleaseAsync(
        QualityThresholdKey key,
        string? by,
        CancellationToken cancellationToken)
        => await SettleAsync(
            key,
            (standing, standings, at) =>
            {
                QualityThresholdSettled released = QualityThresholdSettling.Released(standing, at, by);

                return QualityThresholdStanding.Ordered(key, released.Threshold.Setting.Current, standings)
                    ? Settling.To(released)
                    : Refused(QualitySaying.OutOfOrder(key), QualityThresholdFailure.OutOfOrder);
            },
            cancellationToken);

    private static Settling Refused(string saying, QualityThresholdFailure failure) => new(null, saying, failure);

    private async Task<ServiceResult<QualityThresholdBook, QualityThresholdFailure>> SettleAsync(
        QualityThresholdKey key,
        Func<QualityThresholdStanding, IReadOnlyList<QualityThresholdStanding>, DateTime, Settling> settle,
        CancellationToken cancellationToken)
    {
        Outcome outcome = await writes.AllOrNothingAsync(
            async token =>
            {
                await thresholds.TakeTurnAsync(token);

                IReadOnlyList<QualityThresholdStanding> standings = await StandingsAsync(token);
                QualityThresholdStanding standing = standings.First(held => held.Key == key);
                Settling settling = settle(standing, standings, clock.GetUtcNow().UtcDateTime);

                return settling.Settled is { } settled
                    ? new Outcome(await KeepAsync(standing, settled, token), settling)
                    : new Outcome(null, settling);
            },
            cancellationToken);

        if (outcome.Book is not { } book)
        {
            return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Failure(
                outcome.Settling.Saying!,
                outcome.Settling.Failure);
        }

        if (outcome.Settling.Settled?.Change is not null)
        {
            events.Signal(AppEventName.Quality);
        }

        return ServiceResult<QualityThresholdBook, QualityThresholdFailure>.Success(book);
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

        await thresholds.SaveAsync(settled.Threshold, cancellationToken);
        await changes.AddAsync(recorded, cancellationToken);

        return new QualityThresholdBook(
            standing with
            {
                Setting = settled.Threshold.Setting,
                Stored = true,
                UpdatedBy = settled.Threshold.UpdatedBy,
                ByHand = settled.Threshold.ByHand,
                Measurement = settled.Threshold.Measurement,
            },
            recorded);
    }

    private async Task<IReadOnlyList<QualityThresholdStanding>> StandingsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Over(
            await thresholds.ListAsync(cancellationToken),
            clock.GetUtcNow().UtcDateTime);

    private sealed record Settling(QualityThresholdSettled? Settled, string? Saying, QualityThresholdFailure Failure)
    {
        public static Settling To(QualityThresholdSettled settled) => new(settled, null, default);
    }

    private sealed record Outcome(QualityThresholdBook? Book, Settling Settling);
}
