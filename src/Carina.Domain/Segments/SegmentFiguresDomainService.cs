namespace Carina.Domain.Segments;

/// <summary>
/// Works out the figures detection is shown by, looking up the rounds of learning kept, what viewers did with the
/// buttons and what the catalog would give an evaluation. The version in use is shown by its own figures when it was
/// evaluated, otherwise by those the latest round that measured it took of it, beside what viewers did with its
/// buttons over the last <see cref="SegmentLearning.ReturnsOver"/>; by none when neither is there. What the latest
/// evaluation stands on is that of the highest numbered version evaluated, or, while none has been, what the catalog
/// as it stands would give one.
/// </summary>
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
