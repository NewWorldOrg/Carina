using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

public sealed record LearningDataResponder(int Recordings, long Seconds, long Bytes, int Waiting)
{
    public static LearningDataResponder Of(LearningDataAmount amount)
    {
        ArgumentNullException.ThrowIfNull(amount);

        return new LearningDataResponder(
            amount.Recordings,
            (long)Math.Floor(amount.Duration.TotalSeconds),
            amount.Bytes,
            amount.Waiting);
    }
}

public sealed record CatalogResponder(int Cm, int Opening, int Ending, int ProgrammeFixture, int StationFixture, int Unknown)
{
    public static CatalogResponder Of(CatalogCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return new CatalogResponder(counts.Cm, counts.Opening, counts.Ending, counts.ProgrammeFixture, counts.StationFixture, counts.Unknown);
    }
}

public sealed record SegmentVersionResponder(int Number, SegmentModelVersionState State, bool ByHand, DateTime TrainedAt, DateTime? AdoptedAt)
{
    public static SegmentVersionResponder Of(SegmentModelVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SegmentVersionResponder(version.Number, version.State, version.ByHand, version.TrainedAt, version.AdoptedAt);
    }
}

public sealed record SegmentEvidenceResponder(int Recordings, int Boundaries, bool Enough, int RecordingsNeeded, int BoundariesNeeded)
{
    public static SegmentEvidenceResponder Of(EvaluationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new SegmentEvidenceResponder(
            evidence.Recordings,
            evidence.Boundaries,
            evidence.Enough,
            EvaluationEvidence.RecordingsNeeded,
            EvaluationEvidence.BoundariesNeeded);
    }
}

public sealed record SegmentKindMetricsResponder(SegmentKind Kind, double? Recall);

public sealed record SegmentMetricsResponder(
    double? BoundaryRecallWithin2s,
    double? BoundaryRecallWithin1s,
    IReadOnlyList<SegmentKindMetricsResponder> Kinds,
    double? ReturnedAfterSkipShare,
    int Skips)
{
    public static SegmentMetricsResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentMetricsResponder(
            metrics.BoundaryRecallWithin2s,
            metrics.BoundaryRecallWithin1s,
            [.. metrics.Kinds.Select(kind => new SegmentKindMetricsResponder(kind.Kind, kind.Recall))],
            metrics.ReturnedAfterSkipShare,
            metrics.Skips);
    }
}

public sealed record SegmentMetricsSummaryResponder(double? BoundaryRecallWithin2s)
{
    public static SegmentMetricsSummaryResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentMetricsSummaryResponder(metrics.BoundaryRecallWithin2s);
    }
}

public sealed record SegmentStationResponder(int NetworkId, int ServiceId, int Recordings, SegmentMetricsSummaryResponder Metrics)
{
    public static SegmentStationResponder Of(StationMetrics station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new SegmentStationResponder(
            station.NetworkId.Value,
            station.ServiceId.Value,
            station.Recordings,
            SegmentMetricsSummaryResponder.Of(station.Metrics));
    }
}

public sealed record SegmentRolledBackResponder(DateTime At, double ReturnedAfterSkipShare, double PreviousShare, int Skips);

public sealed record SegmentVersionHistoryResponder(
    int Number,
    SegmentModelVersionState State,
    bool ByHand,
    DateTime TrainedAt,
    DateTime? AdoptedAt,
    DateTime? RetiredAt,
    SegmentRolledBackResponder? RolledBack,
    SegmentMetricsSummaryResponder? Metrics)
{
    public static SegmentVersionHistoryResponder Of(SegmentModelVersion version, SegmentMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SegmentVersionHistoryResponder(
            version.Number,
            version.State,
            version.ByHand,
            version.TrainedAt,
            version.AdoptedAt,
            version.RetiredAt,
            version is { RolledBackAt: DateTime at, Rollback: { } rollback }
                ? new SegmentRolledBackResponder(at, rollback.ReturnedAfterSkipShare, rollback.PreviousShare, rollback.Skips)
                : null,
            metrics is not null ? SegmentMetricsSummaryResponder.Of(metrics) : null);
    }
}

public sealed record SegmentRoundMetricsResponder(double? BoundaryRecallWithin2s, double? BreakRecall)
{
    public static SegmentRoundMetricsResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentRoundMetricsResponder(metrics.BoundaryRecallWithin2s, metrics.BreakRecall);
    }
}

public sealed record SegmentLearningRoundResponder(
    DateOnly Day,
    DateTime TakenAt,
    SegmentTrainingOutcome Outcome,
    int? Candidate,
    AdoptionVerdict? Verdict,
    SegmentRoundMetricsResponder? InUse,
    SegmentEvidenceResponder? Evidence,
    SegmentRoundMetricsResponder? Metrics)
{
    public static SegmentLearningRoundResponder Of(SegmentLearningRound round, SegmentModelVersion? candidate)
    {
        ArgumentNullException.ThrowIfNull(round);

        if (round.Outcome is not SegmentTrainingOutcome outcome)
        {
            throw new ArgumentException("Only a round that says what became of it is answered.", nameof(round));
        }

        return new SegmentLearningRoundResponder(
            round.Day,
            round.TakenAt,
            outcome,
            round.Candidate,
            round.Verdict,
            round.InUseMetrics is { } inUse ? SegmentRoundMetricsResponder.Of(inUse) : null,
            (round.CandidateEvidence ?? candidate?.Evidence) is { } evidence ? SegmentEvidenceResponder.Of(evidence) : null,
            (round.CandidateMetrics ?? candidate?.Metrics) is { } metrics ? SegmentRoundMetricsResponder.Of(metrics) : null);
    }
}

public sealed record SkipThresholdResponder(double Current);

public sealed record SegmentStatusResponder(
    LearningDataResponder LearningData,
    CatalogResponder Catalog,
    SegmentVersionResponder Version,
    SegmentEvidenceResponder Evidence,
    SegmentMetricsResponder? Metrics,
    IReadOnlyList<SegmentStationResponder> Stations,
    IReadOnlyList<SegmentVersionHistoryResponder> Versions,
    SkipThresholdResponder SkipThreshold,
    SegmentLearningRoundResponder? LastRound,
    DateTime NextRoundNotBefore)
{
    public static SegmentStatusResponder Of(SegmentStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        return new SegmentStatusResponder(
            LearningDataResponder.Of(status.LearningData),
            CatalogResponder.Of(status.Catalog),
            SegmentVersionResponder.Of(status.Version),
            SegmentEvidenceResponder.Of(status.Evidence),
            status.Metrics is { } metrics ? SegmentMetricsResponder.Of(metrics) : null,
            [.. (status.Version.Stations ?? []).Select(SegmentStationResponder.Of)],
            [.. status.Versions.Select(version => SegmentVersionHistoryResponder.Of(version, version.Number == status.Version.Number ? status.Metrics : version.Metrics))],
            new SkipThresholdResponder(status.Settings.SkipThreshold),
            status.LastRound is { } round ? SegmentLearningRoundResponder.Of(round, status.LastCandidate) : null,
            status.NextRoundNotBefore);
    }
}
