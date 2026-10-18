using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

/// <summary>
/// The figures of one station's confirmed recordings.
/// </summary>
public sealed record StationMetrics(NetworkId NetworkId, ServiceId ServiceId, int ConfirmedRecordings, SegmentMetrics Metrics);

/// <summary>
/// How a version did on the confirmed recordings: its figures over all of them, none without any; over
/// each station's, for the stations with one, by network and service; what that stands on; and the
/// threshold a skip button is recommended to be offered from, none without a recording judged.
/// </summary>
public sealed record SegmentEvaluation(
    SegmentMetrics? Metrics,
    IReadOnlyList<StationMetrics> Stations,
    EvaluationEvidence Evidence,
    double? RecommendedThreshold);
