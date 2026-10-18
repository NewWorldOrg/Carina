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
/// What an evaluation stands on: the confirmed recordings judged and the boundaries of breaks their answers
/// draw, whether that is enough to put a version in use, and how many of each it takes.
/// </summary>
public sealed record SegmentEvidenceResponder(int ConfirmedRecordings, int Boundaries, bool Enough, int RecordingsNeeded, int BoundariesNeeded)
{
    public static SegmentEvidenceResponder Of(EvaluationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new SegmentEvidenceResponder(
            evidence.ConfirmedRecordings,
            evidence.Boundaries,
            evidence.Enough,
            EvaluationEvidence.RecordingsNeeded,
            EvaluationEvidence.BoundariesNeeded);
    }
}

/// <summary>
/// The seconds of one kind both answered and detected, as a share of those answered as it (recall) and of
/// those detected as it (precision); null where there were none to share.
/// </summary>
public sealed record SegmentKindMetricsResponder(SegmentKind Kind, double? Recall, double? Precision);

/// <summary>
/// How the version in use detects the confirmed recordings: the share of answered boundaries found within two
/// seconds and within one, the extra boundaries per recording, the seconds of programme a button would skip per
/// hour, the seconds of each kind found, and the share of skips on its buttons viewers came back from over the
/// last seven days, with how many skips that is of.
/// </summary>
public sealed record SegmentMetricsResponder(
    double? BoundaryRecallWithin2s,
    double? BoundaryRecallWithin1s,
    double ExtraBoundariesPerRecording,
    double ProgrammeSkippedSecondsPerHour,
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
            metrics.ExtraBoundariesPerRecording,
            metrics.ProgrammeSkippedSecondsPerHour,
            [.. metrics.Kinds.Select(kind => new SegmentKindMetricsResponder(kind.Kind, kind.Recall, kind.Precision))],
            metrics.ReturnedAfterSkipShare,
            metrics.Skips);
    }
}

/// <summary>
/// The two figures a station or a version is compared by: the share of answered boundaries found within two
/// seconds, and the seconds of programme a button would skip per hour.
/// </summary>
public sealed record SegmentMetricsSummaryResponder(double? BoundaryRecallWithin2s, double ProgrammeSkippedSecondsPerHour)
{
    public static SegmentMetricsSummaryResponder Of(SegmentMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return new SegmentMetricsSummaryResponder(metrics.BoundaryRecallWithin2s, metrics.ProgrammeSkippedSecondsPerHour);
    }
}

/// <summary>
/// How the version in use detects one station's confirmed recordings.
/// </summary>
public sealed record SegmentStationResponder(int NetworkId, int ServiceId, int ConfirmedRecordings, SegmentMetricsSummaryResponder Metrics)
{
    public static SegmentStationResponder Of(StationMetrics station)
    {
        ArgumentNullException.ThrowIfNull(station);

        return new SegmentStationResponder(
            station.NetworkId.Value,
            station.ServiceId.Value,
            station.ConfirmedRecordings,
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
/// and taken out of use, why when a rollback took it out, and its two figures when it was evaluated on a
/// confirmed recording.
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
/// The threshold a skip button is offered from, and the one the evaluation of the version in use recommends,
/// null without one.
/// </summary>
public sealed record SkipThresholdResponder(double Current, double? Recommended);

/// <summary>
/// Where CM, OP and ED detection stands.
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
    SkipThresholdResponder SkipThreshold)
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
            new SkipThresholdResponder(status.Settings.SkipThreshold, status.Version.RecommendedThreshold));
    }
}
