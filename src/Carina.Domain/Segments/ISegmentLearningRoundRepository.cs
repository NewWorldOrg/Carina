namespace Carina.Domain.Segments;

public interface ISegmentLearningRoundRepository
{
    Task<bool> TakenAsync(DateOnly day, CancellationToken cancellationToken);

    Task<bool> TakeAsync(SegmentLearningRound round, CancellationToken cancellationToken);

    Task<DateTime?> LastLearnedAsync(CancellationToken cancellationToken);

    Task<SegmentLearningRound?> LatestAsync(CancellationToken cancellationToken);

    Task<SegmentLearningRound?> LatestMeasuredAsync(int version, CancellationToken cancellationToken);

    Task<IAsyncDisposable?> HoldAsync(CancellationToken cancellationToken);
}
