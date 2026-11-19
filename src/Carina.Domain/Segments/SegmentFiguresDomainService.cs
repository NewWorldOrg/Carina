namespace Carina.Domain.Segments;

public sealed class SegmentFiguresDomainService(
    ISegmentLearningRoundRepository rounds,
    ISkipEventRepository skips,
    ICatalogEvidenceReader evidence,
    TimeProvider clock)
{
    public async Task<SegmentMetrics?> InUseAsync(SegmentModelVersion active, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(active);

        SegmentMetrics? measured = active.Metrics ?? (await rounds.LatestMeasuredAsync(active.Number, cancellationToken))?.InUseMetrics;

        if (measured is not { } evaluated)
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        return evaluated.WithReturns(await skips.ReturnsAsync(active.Number, now - SegmentLearning.ReturnsOver, now, cancellationToken));
    }

    public async Task<EvaluationEvidence> EvidenceAsync(IReadOnlyList<SegmentModelVersion> kept, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kept);

        return kept.OrderByDescending(version => version.Number).FirstOrDefault(version => version.Evidence is not null)?.Evidence
               ?? await evidence.ReadAsync(cancellationToken);
    }
}
