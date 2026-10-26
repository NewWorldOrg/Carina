using System.Text.Json;
using System.Text.Json.Serialization;

using Carina.Domain.Channels;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The shapes the segment tables keep in jsonb: the parameters of a version of the model, the figures of its
/// evaluation and what a rollback from it measured, the stretches of a detection and its boundary candidates,
/// seconds written as numbers to <see cref="SecondDigits"/> places and kinds by their names.
/// </summary>
internal static class SegmentJson
{
    public const int SecondDigits = 2;

    public static string Written(SegmentWeights weights)
        => Serialised(new WeightsShape(weights.Classes.ToDictionary(
            pair => pair.Key.ToString(),
            pair => new ClassShape(pair.Value.Bias, pair.Value.Features.ToDictionary()))));

    public static SegmentWeights Weights(string stored)
        => new(Deserialised<WeightsShape>(stored).Classes.ToDictionary(
            pair => Enum.Parse<SegmentKind>(pair.Key),
            pair => new SegmentClassWeights(pair.Value.Bias, pair.Value.Features)));

    public static string Written(IReadOnlyDictionary<string, double> offsets) => Serialised(offsets);

    public static IReadOnlyDictionary<string, double> Offsets(string stored) => Deserialised<Dictionary<string, double>>(stored);

    public static string Written(SegmentThresholds thresholds)
        => Serialised(new ThresholdsShape(
            thresholds.SilenceDbfs,
            thresholds.BlackLevel,
            thresholds.SceneChange,
            thresholds.GridToleranceSec,
            thresholds.GridPenalty,
            thresholds.Lambda,
            thresholds.LambdaStation,
            thresholds.LambdaSeries));

    public static SegmentThresholds Thresholds(string stored)
    {
        ThresholdsShape shape = Deserialised<ThresholdsShape>(stored);

        return new SegmentThresholds(
            shape.SilenceDbfs,
            shape.BlackLevel,
            shape.SceneChange,
            shape.GridToleranceSec,
            shape.GridPenalty,
            shape.Lambda,
            shape.LambdaStation,
            shape.LambdaSeries);
    }

    public static string Written(IReadOnlyDictionary<string, FeatureScale> standardisation)
        => Serialised(standardisation.ToDictionary(pair => pair.Key, pair => new ScaleShape(pair.Value.Mean, pair.Value.Sd), StringComparer.Ordinal));

    public static IReadOnlyDictionary<string, FeatureScale> Standardisation(string stored)
        => Deserialised<Dictionary<string, ScaleShape>>(stored).ToDictionary(pair => pair.Key, pair => new FeatureScale(pair.Value.Mean, pair.Value.Sd), StringComparer.Ordinal);

    public static string Written(SegmentMetrics metrics) => Serialised(MetricsShape.Of(metrics));

    public static SegmentMetrics Metrics(string stored) => Deserialised<MetricsShape>(stored).Read();

    public static string Written(IReadOnlyList<StationMetrics> stations)
        => Serialised(stations.Select(station => new StationShape(station.NetworkId.Value, station.ServiceId.Value, station.Recordings, MetricsShape.Of(station.Metrics))).ToList());

    public static IReadOnlyList<StationMetrics> Stations(string stored)
        => [.. Deserialised<List<StationShape>>(stored).Select(shape => new StationMetrics(new NetworkId(shape.NetworkId), new ServiceId(shape.ServiceId), shape.Recordings, shape.Metrics.Read()))];

    public static string Written(EvaluationEvidence evidence)
        => Serialised(new EvidenceShape(evidence.Recordings, evidence.Boundaries, evidence.LabelsUntil, evidence.Openings, evidence.Endings));

    public static EvaluationEvidence Evidence(string stored)
    {
        EvidenceShape shape = Deserialised<EvidenceShape>(stored);

        return new EvaluationEvidence(shape.Recordings, shape.Boundaries, shape.LabelsUntil, shape.Openings, shape.Endings);
    }

    public static string Written(SegmentRollback rollback)
        => Serialised(new RollbackShape(rollback.ReturnedAfterSkipShare, rollback.PreviousShare, rollback.Skips, rollback.BackTo));

    public static SegmentRollback Rollback(string stored)
    {
        RollbackShape shape = Deserialised<RollbackShape>(stored);

        return new SegmentRollback(shape.ReturnedAfterSkipShare, shape.PreviousShare, shape.Skips, shape.BackTo);
    }

    public static string Written(IReadOnlyList<SegmentSpan> segments)
        => Serialised(segments.Select(segment => new SpanShape(Seconds(segment.Starts), Seconds(segment.Ends), segment.Kind, segment.Probability)).ToList());

    public static IReadOnlyList<SegmentSpan> Segments(string stored)
        => [.. Deserialised<List<SpanShape>>(stored).Select(shape => new SegmentSpan(At(shape.StartsAtSec), At(shape.EndsAtSec), shape.Kind, shape.Probability))];

    public static string Written(IReadOnlyList<TimeSpan> seconds) => Serialised(seconds.Select(Seconds).ToList());

    public static IReadOnlyList<TimeSpan> Seconds(string stored) => [.. Deserialised<List<double>>(stored).Select(At)];

    public static ValueComparer<T> ComparedAsWritten<T>(Func<T, string> written)
        where T : class
        => new(
            (left, right) => left == right || (left != null && right != null && written(left) == written(right)),
            value => StringComparer.Ordinal.GetHashCode(written(value)),
            value => value);

    private static double Seconds(TimeSpan at) => Math.Round(at.TotalSeconds, SecondDigits, MidpointRounding.AwayFromZero);

    private static TimeSpan At(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));

    private static string Serialised<T>(T shape) => JsonSerializer.Serialize(shape, ProgrammeJson.Options);

    private static T Deserialised<T>(string stored)
        => JsonSerializer.Deserialize<T>(stored, ProgrammeJson.Options)
           ?? throw new JsonException($"A segment table keeps a {typeof(T).Name} where it found null.");

    private sealed record WeightsShape(Dictionary<string, ClassShape> Classes);

    private sealed record ClassShape(double Bias, Dictionary<string, double> Features);

    private sealed record ThresholdsShape(
        double SilenceDbfs,
        double BlackLevel,
        double SceneChange,
        double GridToleranceSec,
        double GridPenalty,
        double Lambda,
        double LambdaStation,
        double LambdaSeries);

    private sealed record ScaleShape(double Mean, double Sd);

    private sealed record MetricsShape(
        double? BoundaryRecallWithin2s,
        double? BoundaryRecallWithin1s,
        List<KindShape> Kinds,
        double? ReturnedAfterSkipShare,
        int Skips)
    {
        public static MetricsShape Of(SegmentMetrics metrics)
            => new(
                metrics.BoundaryRecallWithin2s,
                metrics.BoundaryRecallWithin1s,
                [.. metrics.Kinds.Select(kind => new KindShape(kind.Kind, kind.Recall))],
                metrics.ReturnedAfterSkipShare,
                metrics.Skips);

        public SegmentMetrics Read()
            => new(
                BoundaryRecallWithin2s,
                BoundaryRecallWithin1s,
                [.. Kinds.Select(kind => new KindMetrics(kind.Kind, kind.Recall))],
                ReturnedAfterSkipShare,
                Skips);
    }

    private sealed record KindShape(SegmentKind Kind, double? Recall);

    private sealed record StationShape(int NetworkId, int ServiceId, int Recordings, MetricsShape Metrics);

    /// <summary>
    /// What an evaluation stands on, as kept; one kept before the openings and endings were counted holds none.
    /// </summary>
    private sealed record EvidenceShape(int Recordings, int Boundaries, DateTime? LabelsUntil, int Openings = 0, int Endings = 0);

    private sealed record RollbackShape(double ReturnedAfterSkipShare, double PreviousShare, int Skips, int BackTo);

    private sealed record SpanShape(
        double StartsAtSec,
        double EndsAtSec,
        SegmentKind Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Probability);
}
