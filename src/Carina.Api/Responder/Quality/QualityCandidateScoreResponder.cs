using Carina.Api.Responder.Scans;
using Carina.Domain.Channels;

namespace Carina.Api.Responder.Quality;

public sealed record QualityCandidateMeasuredResponder(
    double LockRate,
    int? CarrierToNoiseLowestMilliDecibels,
    double? BitErrorRateHighest,
    long Samples,
    DateTimeOffset MeasuredFrom,
    DateTimeOffset MeasuredUntil)
{
    public static QualityCandidateMeasuredResponder? Of(CandidateScore? score)
        => score is null
            ? null
            : new QualityCandidateMeasuredResponder(
                score.LockRate,
                score.CarrierToNoiseLowestMilliDecibels,
                score.BitErrorRateHighest,
                score.Samples,
                score.MeasuredFrom,
                score.MeasuredUntil);
}

public sealed record QualityCandidateScoreResponder(
    Guid CandidateId,
    int NetworkId,
    int ServiceId,
    ScanTargetResponder Target,
    bool IsSelected,
    QualityCandidateMeasuredResponder? Score,
    DateTimeOffset? EvaluatedAt)
{
    public static QualityCandidateScoreResponder Of(CandidateChannel candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return new QualityCandidateScoreResponder(
            candidate.Id.Value,
            candidate.NetworkId.Value,
            candidate.ServiceId.Value,
            ScanTargetResponder.Of(candidate.Tuning),
            candidate.IsSelected,
            QualityCandidateMeasuredResponder.Of(candidate.Score),
            candidate.Score?.EvaluatedAt);
    }
}

public sealed record QualityCandidateScoreListResponder(IReadOnlyList<QualityCandidateScoreResponder> Items)
{
    public static QualityCandidateScoreListResponder Of(IReadOnlyList<CandidateChannel> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return new QualityCandidateScoreListResponder([.. candidates.Select(QualityCandidateScoreResponder.Of)]);
    }
}
