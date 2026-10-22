namespace Carina.Domain.Segments;

/// <summary>
/// Where the rounds of learning finished are kept, one per day. Nothing is removed from it.
/// </summary>
public interface ISegmentLearningRoundRepository
{
    /// <summary>
    /// Whether the round of <paramref name="day"/> has been finished.
    /// </summary>
    Task<bool> TakenAsync(DateOnly day, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps <paramref name="round"/> as finished unless the round of its day has been, and says whether it was kept.
    /// </summary>
    Task<bool> TakeAsync(SegmentLearningRound round, CancellationToken cancellationToken);

    /// <summary>
    /// When the latest round that kept a candidate was taken; none while no round has.
    /// </summary>
    Task<DateTime?> LastLearnedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest round finished that says what became of it; none while no round does.
    /// </summary>
    Task<SegmentLearningRound?> LatestAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The latest round finished that kept the figures of the version in use it was judged against; none while no
    /// round has, as a round that learned nothing keeps none.
    /// </summary>
    Task<SegmentLearningRound?> LatestMeasuredAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Holds the turn to take a round until what it answers is disposed, so that no other process takes one
    /// meanwhile; none while another holds it.
    /// </summary>
    Task<IAsyncDisposable?> HoldAsync(CancellationToken cancellationToken);
}
