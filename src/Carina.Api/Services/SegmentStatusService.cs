using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Where CM, OP and ED detection stands, as the settings screen shows it: the settings, the learning data and
/// the catalog; the version in use, and its figures beside what viewers did with its buttons lately; what the
/// latest evaluation stands on, or what the answers kept would give one while no version has been evaluated;
/// and every version kept, the highest number first.
/// </summary>
public sealed record SegmentStatus(
    SegmentSettingsStanding Settings,
    LearningDataAmount LearningData,
    CatalogCounts Catalog,
    SegmentModelVersion Version,
    SegmentMetrics? Metrics,
    EvaluationEvidence Evidence,
    IReadOnlyList<SegmentModelVersion> Versions);

public sealed class SegmentStatusService(
    ILearningDataAmountReader learningData,
    ICatalogCountsReader catalog,
    ISegmentModelVersionRepository versions,
    ISegmentSettingsRepository settings,
    ISkipEventRepository skips,
    ISegmentAnswerRepository answers,
    TimeProvider clock)
{
    public async Task<ServiceResult<SegmentStatus>> ReadAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SegmentModelVersion> kept = [.. (await versions.ListAsync(cancellationToken)).OrderByDescending(version => version.Number)];
        SegmentModelVersion active = kept.Single(version => version.State is SegmentModelVersionState.Active);

        return ServiceResult<SegmentStatus>.Success(new SegmentStatus(
            SegmentSettingsStanding.Over(await settings.ReadAsync(cancellationToken)),
            await learningData.ReadAsync(cancellationToken),
            await catalog.ReadAsync(cancellationToken),
            active,
            await MetricsAsync(active, cancellationToken),
            kept.FirstOrDefault(version => version.Evidence is not null)?.Evidence ?? await answers.EvidenceAsync(cancellationToken),
            kept));
    }

    private async Task<SegmentMetrics?> MetricsAsync(SegmentModelVersion active, CancellationToken cancellationToken)
    {
        if (active.Metrics is not { } evaluated)
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        return evaluated.WithReturns(await skips.ReturnsAsync(active.Number, now - SegmentLearning.ReturnsOver, now, cancellationToken));
    }
}
