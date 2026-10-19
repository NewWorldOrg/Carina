using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

/// <summary>
/// The figures of one station's recordings judged, and how many were.
/// </summary>
public sealed record StationMetrics(NetworkId NetworkId, ServiceId ServiceId, int Recordings, SegmentMetrics Metrics);

/// <summary>
/// How a version did on the recordings judged: its figures over all of them, none without any; over each
/// station's, for the stations with one, by network and service; and what that stands on.
/// </summary>
public sealed record SegmentEvaluation(
    SegmentMetrics? Metrics,
    IReadOnlyList<StationMetrics> Stations,
    EvaluationEvidence Evidence);
