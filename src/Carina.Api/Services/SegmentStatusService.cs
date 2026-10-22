using Carina.Api.Common;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

/// <summary>
/// Where CM, OP and ED detection stands, as the settings screen shows it: the settings, the learning data and
/// the catalog; the version in use, and its figures beside what viewers did with its buttons lately, taken from the
/// latest round when the version was never evaluated itself but was the one that round measured; what the
/// latest evaluation stands on, or what the catalog as it stands would give one while no version has been
/// evaluated; every version kept, the highest number first; the latest round of learning that says what became of
/// it, with the candidate it kept when that is still kept; and the first moment the next round may begin.
/// </summary>
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
    ISkipEventRepository skips,
    ICatalogEvidenceReader evidence,
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
            await MetricsAsync(active, last, cancellationToken),
            kept.FirstOrDefault(version => version.Evidence is not null)?.Evidence ?? await evidence.ReadAsync(cancellationToken),
            kept,
            last,
            kept.FirstOrDefault(version => version.Number == last?.Candidate),
            SegmentLearningRound.NextNotBefore(now, await rounds.TakenAsync(SegmentLearningRound.DayOf(now), cancellationToken))));
    }

    private async Task<SegmentMetrics?> MetricsAsync(SegmentModelVersion active, SegmentLearningRound? last, CancellationToken cancellationToken)
    {
        if ((active.Metrics ?? MeasuredBy(active, last)) is not { } evaluated)
        {
            return null;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        return evaluated.WithReturns(await skips.ReturnsAsync(active.Number, now - SegmentLearning.ReturnsOver, now, cancellationToken));
    }

    /// <summary>
    /// The figures <paramref name="last"/> measured the version in use by, when it was put in use no later than that
    /// round was taken and so was the version that round measured; none otherwise.
    /// </summary>
    private static SegmentMetrics? MeasuredBy(SegmentModelVersion active, SegmentLearningRound? last)
        => last is { InUseMetrics: { } measured } && active.AdoptedAt <= last.TakenAt ? measured : null;
}
