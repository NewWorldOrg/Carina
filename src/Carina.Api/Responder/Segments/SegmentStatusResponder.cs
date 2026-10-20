using Carina.Api.Services;
using Carina.Domain.Segments;

namespace Carina.Api.Responder.Segments;

/// <summary>
/// How much learning data is kept: the recordings it was taken from, including those thrown away since, the
/// whole seconds of them read, the bytes the segment tables take in the database, and the ended recordings
/// still waiting to be read.
/// </summary>
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

/// <summary>
/// How many entries the catalog of repeated sound holds of each kind.
/// </summary>
public sealed record CatalogResponder(int Cm, int Opening, int Ending, int ProgrammeFixture, int StationFixture, int Unknown)
{
    public static CatalogResponder Of(CatalogCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);

        return new CatalogResponder(counts.Cm, counts.Opening, counts.Ending, counts.ProgrammeFixture, counts.StationFixture, counts.Unknown);
    }
}

/// <summary>
/// The version of the model in use: its number, where it stands, whether it was chosen by hand, and when it was
/// made and put in use.
/// </summary>
public sealed record SegmentVersionResponder(int Number, SegmentModelVersionState State, bool ByHand, DateTime TrainedAt, DateTime? AdoptedAt)
{
    public static SegmentVersionResponder Of(SegmentModelVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new SegmentVersionResponder(version.Number, version.State, version.ByHand, version.TrainedAt, version.AdoptedAt);
    }
}

/// <summary>
/// What an evaluation stands on: the recordings the catalog is sure of an advert in and the outer edges of the pods of
/// those adverts, not the joins between the adverts in a pod, whether that is enough to put a version in use, and how many of each it takes.
/// </summary>
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

/// <summary>
/// The share of the seconds of one kind the catalog is sure of that were detected as it; null where the catalog is
/// sure of none.
/// </summary>
public sealed record SegmentKindMetricsResponder(SegmentKind Kind, double? Recall);

/// <summary>
/// How the version in use detects the recordings it was judged on, against what the catalog is sure of in them: the
/// share of the boundaries, the outer edges of each pod of adverts and of each opening and ending, found within two
/// seconds and within one, the seconds found of each kind it was measured
/// on, and the share of skips on its buttons viewers came back from over the last seven days, with how many skips
/// that is of.
/// </summary>
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

/// <summary>
/// The figure a station or a version is compared by: the share of the boundaries the catalog is sure of, the outer
/// edges of each pod of adverts and of each opening and ending, found within two seconds.
/// </summary>
public sealed record SegmentMetricsSummaryResponder(double? BoundaryRecallWithin2s)
{
    public static SegmentMetricsSummaryResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentMetricsSummaryResponder(metrics.BoundaryRecallWithin2s);
    }
}

/// <summary>
/// How the version in use detects one station's recordings, and how many of them it was judged on.
/// </summary>
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

/// <summary>
/// Why a version was taken out of use by going back to the one before it: when, the share of skips viewers
/// came back from on it and on the version before, and how many skips that was of.
/// </summary>
public sealed record SegmentRolledBackResponder(DateTime At, double ReturnedAfterSkipShare, double PreviousShare, int Skips);

/// <summary>
/// One version kept: its number, where it stands, whether it was chosen by hand, when it was made, put in use
/// and taken out of use, why when a rollback took it out, and its figure when it was evaluated on a recording.
/// </summary>
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
    public static SegmentVersionHistoryResponder Of(SegmentModelVersion version)
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
            version.Metrics is { } metrics ? SegmentMetricsSummaryResponder.Of(metrics) : null);
    }
}

/// <summary>
/// The figures a round of learning compares a candidate and the version in use by: the share of the boundaries the
/// catalog is sure of, the outer edges of each pod of adverts and of each opening and ending, found within two seconds, and the share of the seconds of adverts it is sure of detected as
/// breaks.
/// </summary>
public sealed record SegmentRoundMetricsResponder(double? BoundaryRecallWithin2s, double? BreakRecall)
{
    public static SegmentRoundMetricsResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentRoundMetricsResponder(metrics.BoundaryRecallWithin2s, metrics.BreakRecall);
    }
}

/// <summary>
/// The latest round of learning: the day it was for, when it was taken and what became of it; when it learned a
/// candidate, the candidate's number, the verdict on it and the figures of the version in use on the same
/// recordings; and, while that candidate is still kept, what its evaluation stands on and its figures.
/// </summary>
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
            candidate?.Evidence is { } evidence ? SegmentEvidenceResponder.Of(evidence) : null,
            candidate?.Metrics is { } metrics ? SegmentRoundMetricsResponder.Of(metrics) : null);
    }
}

/// <summary>
/// The threshold a skip button is offered from.
/// </summary>
public sealed record SkipThresholdResponder(double Current);

/// <summary>
/// Where CM, OP and ED detection stands, with the latest round of learning, none before the first, and the first
/// moment the next round may begin, whether learning is on or off.
/// </summary>
public sealed record SegmentStatusResponder(
    bool Learning,
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
            status.Settings.Learning,
            LearningDataResponder.Of(status.LearningData),
            CatalogResponder.Of(status.Catalog),
            SegmentVersionResponder.Of(status.Version),
            SegmentEvidenceResponder.Of(status.Evidence),
            status.Metrics is { } metrics ? SegmentMetricsResponder.Of(metrics) : null,
            [.. (status.Version.Stations ?? []).Select(SegmentStationResponder.Of)],
            [.. status.Versions.Select(SegmentVersionHistoryResponder.Of)],
            new SkipThresholdResponder(status.Settings.SkipThreshold),
            status.LastRound is { } round ? SegmentLearningRoundResponder.Of(round, status.LastCandidate) : null,
            status.NextRoundNotBefore);
    }
}
