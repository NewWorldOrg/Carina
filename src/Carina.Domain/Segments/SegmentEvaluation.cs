using Carina.Domain.Channels;

namespace Carina.Domain.Segments;

public sealed record StationMetrics(NetworkId NetworkId, ServiceId ServiceId, int Recordings, SegmentMetrics Metrics);

public sealed record SegmentEvaluation(
    SegmentMetrics? Metrics,
    IReadOnlyList<StationMetrics> Stations,
    EvaluationEvidence Evidence);
