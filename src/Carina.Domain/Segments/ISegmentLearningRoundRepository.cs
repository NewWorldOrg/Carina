namespace Carina.Domain.Segments;

/// <summary>
/// Where the rounds of learning taken are kept, one per day. Nothing is removed from it.
/// </summary>
public interface ISegmentLearningRoundRepository
{
    /// <summary>
    /// Whether the round of <paramref name="day"/> has been taken.
    /// </summary>
    Task<bool> TakenAsync(DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps <paramref name="round"/> unless the round of its day has been taken, and says whether it was kept.
    /// </summary>
    Task<bool> TakeAsync(SegmentLearningRound round, CancellationToken cancellationToken);
}
