using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public sealed record SegmentStatus(
    SegmentSettingsStanding Settings,
    LearningDataAmount LearningData,
    CatalogCounts Catalog,
    SegmentModelVersion Version,
    SegmentMetrics? Metrics,
    EvaluationEvidence Evidence,
    IReadOnlyList<SegmentModelVersion> Versions,
    SegmentLearningRound? LastRound,
    SegmentModelVersion? LastCandidate,
    DateTime NextRoundNotBefore);

public sealed class SegmentStatusService(
    ILearningDataAmountReader learningData,
    ICatalogCountsReader catalog,
    ISegmentModelVersionRepository versions,
    ISegmentSettingsRepository settings,
    SegmentFiguresDomainService figures,
    ISegmentLearningRoundRepository rounds,
    TimeProvider clock)
{
    public async Task<ServiceResult<SegmentStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentModelVersion> kept = [.. (await versions.ListAsync(cancellationToken)).OrderByDescending(version => version.Number)];
        SegmentModelVersion active = kept.Single(version => version.State is SegmentModelVersionState.Active);
        SegmentLearningRound? last = await rounds.LatestAsync(cancellationToken);
        DateTime now = clock.GetUtcNow().UtcDateTime;

        return ServiceResult<SegmentStatus>.Success(new SegmentStatus(
            SegmentSettingsStanding.Over(await settings.ReadAsync(cancellationToken)),
            await learningData.ReadAsync(cancellationToken),
            await catalog.ReadAsync(cancellationToken),
            active,
            await figures.InUseAsync(active, cancellationToken),
            await figures.EvidenceAsync(kept, cancellationToken),
            kept,
            last,
            kept.FirstOrDefault(version => version.Number == last?.Candidate),
            SegmentLearningRound.NextNotBefore(now, await rounds.TakenAsync(SegmentLearningRound.DayOf(now), cancellationToken))));
    }
}
