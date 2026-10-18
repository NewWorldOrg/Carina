using System.Text.Json;
using System.Text.Json.Serialization;

using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The shapes the segment tables keep in jsonb: the parameters of a version of the model, the stretches of a
/// detection and its boundary candidates, seconds written as numbers to <see cref="SecondDigits"/> places and
/// kinds by their names.
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

    private sealed record SpanShape(
        double StartsAtSec,
        double EndsAtSec,
        SegmentKind Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Probability);
}
