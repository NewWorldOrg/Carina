namespace Carina.Domain.Segments;

/// <summary>
/// Decides whether the day's round of learning is still to be taken and whether it has anything new to learn from,
/// looking up the rounds kept and when what learning reads last changed. A round is taken once a day: once the day's
/// round is kept, finished or failed, no other is taken until the next day begins (<see cref="SegmentLearningRound.DayOf"/>).
/// A round learns only when something learning reads changed since the last round that kept a candidate
/// (<see cref="SegmentLearningRound.HasNews"/>).
/// </summary>
public sealed class SegmentLearningRoundDomainService(
    ISegmentLearningRoundRepository rounds,
    ILearningMaterialChanges changes)
{
    public async Task<bool> IsDueAsync(DateOnly day, CancellationToken cancellationToken)
        => !await rounds.TakenAsync(day, cancellationToken);

    public async Task<bool> HasNewsAsync(CancellationToken cancellationToken)
        => SegmentLearningRound.HasNews(
            await rounds.LastLearnedAsync(cancellationToken),
            await changes.LastChangedAsync(cancellationToken));
}
