namespace Carina.Domain.Segments;

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
