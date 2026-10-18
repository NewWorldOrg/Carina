using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentModelTests
{
    private const string FirstWeights =
        """{ "classes": { "Break": { "bias": -2.5, "features": { "isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0 } } } }""";

    private const string InitialThresholds =
        """{ "silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10 }""";

    private static readonly JsonSerializerOptions Jsonb = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string Station = SegmentModel.StationKey(new NetworkId(32736), new ServiceId(1024));

    [Fact(DisplayName = "the first version carries the hand-set weights, no offsets, the initial thresholds and an identity scale for what it weighs")]
    public void TheFirstVersionIsHandSet()
    {
        SegmentModel first = SegmentModel.First;
        SegmentClassWeights weights = first.Weights.Classes[SegmentKind.Break];

        Assert.Equal(-2.5, weights.Bias);
        Assert.Equal(
            new Dictionary<string, double>
            {
                [SegmentFeature.IsMultipleOf15s] = 2.0,
                [SegmentFeature.EdgeSilence] = 1.5,
                [SegmentFeature.LogoShare] = -3.0,
                [SegmentFeature.CatalogCmShare] = 4.0,
            }.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            weights.Features.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Empty(first.StationOffsets);
        Assert.Empty(first.SeriesOffsets);
        Assert.Equal(SegmentThresholds.Initial, first.Thresholds);
        Assert.Equal(new FeatureScale(0, 1), FeatureScale.Identity);
        Assert.All(weights.Features.Keys, name => Assert.Equal(FeatureScale.Identity, first.Standardisation[name]));
        Assert.Equal(2, first.Kinds);
    }

    [Fact(DisplayName = "the weights read from the jsonb shape of the design are the first version's, and are written back to the same shape")]
    public void TheWeightsRoundTripThroughTheirShape()
    {
        SegmentWeights read = JsonSerializer.Deserialize<SegmentWeights>(FirstWeights, Jsonb)!;

        Assert.Equal(SegmentModel.First.Weights, read);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(FirstWeights), JsonSerializer.SerializeToNode(read, Jsonb)));
    }

    [Fact(DisplayName = "the thresholds read from the jsonb shape of the design are the initial ones, and are written back to the same shape")]
    public void TheThresholdsRoundTripThroughTheirShape()
    {
        SegmentThresholds read = JsonSerializer.Deserialize<SegmentThresholds>(InitialThresholds, Jsonb)!;

        Assert.Equal(SegmentThresholds.Initial, read);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(InitialThresholds), JsonSerializer.SerializeToNode(read, Jsonb)));
    }

    [Fact(DisplayName = "the standardisation and both offsets round-trip through their jsonb shapes into an equal model")]
    public void TheStandardisationAndOffsetsRoundTrip()
    {
        const string standardisation = """{ "length": { "mean": 31.2, "sd": 18.4 }, "isMultipleOf15s": { "mean": 0.2, "sd": 0.4 } }""";
        const string stations = """{ "32736:1024": 0.3 }""";
        const string series = """{ "架空の物語": -0.2 }""";
        SegmentWeights weights = new(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = new(-1.0, new Dictionary<string, double> { [SegmentFeature.Length] = 0.5, [SegmentFeature.IsMultipleOf15s] = 1.0 }),
        });

        SegmentModel read = new(
            weights,
            JsonSerializer.Deserialize<Dictionary<string, double>>(stations, Jsonb)!,
            JsonSerializer.Deserialize<Dictionary<string, double>>(series, Jsonb)!,
            SegmentThresholds.Initial,
            JsonSerializer.Deserialize<Dictionary<string, FeatureScale>>(standardisation, Jsonb)!);
        SegmentModel again = new(
            weights,
            new Dictionary<string, double> { [Station] = 0.3 },
            new Dictionary<string, double> { ["架空の物語"] = -0.2 },
            SegmentThresholds.Initial,
            new Dictionary<string, FeatureScale>
            {
                [SegmentFeature.IsMultipleOf15s] = new(0.2, 0.4),
                [SegmentFeature.Length] = new(31.2, 18.4),
            });

        Assert.Equal(again, read);
        Assert.Equal(again.GetHashCode(), read.GetHashCode());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(standardisation), JsonSerializer.SerializeToNode(read.Standardisation, Jsonb)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(stations), JsonSerializer.SerializeToNode(read.StationOffsets, Jsonb)));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(series), JsonSerializer.SerializeToNode(read.SeriesOffsets, Jsonb)));
    }

    [Fact(DisplayName = "the first version scores a 15-second piece between two silent cuts with no logo above even, and the same piece under the logo below it")]
    public void TheFirstVersionScoresByTheRuleItWasMadeFrom()
    {
        SegmentModel first = SegmentModel.First;

        double pod = first.BreakScore(Features((SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.LogoShare, 0)), Station, null);
        double branded = first.BreakScore(Features((SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.LogoShare, 1)), Station, null);
        double heard = first.BreakScore(Features((SegmentFeature.CatalogCmShare, 1)), Station, null);
        double plain = first.BreakScore(Features(), Station, null);

        Assert.Equal(1.0, pod, 12);
        Assert.Equal(-2.0, branded, 12);
        Assert.Equal(1.5, heard, 12);
        Assert.Equal(-2.5, plain, 12);
        Assert.True(SegmentModel.Chance(pod) > 0.5);
        Assert.True(SegmentModel.Chance(branded) < 0.5);
        Assert.Equal(1 / (1 + Math.Exp(-1.0)), SegmentModel.Chance(pod), 12);
    }

    [Fact(DisplayName = "a feature is standardised with the mean and sd the version carries for it, and one not known counts as its mean")]
    public void FeaturesAreStandardisedWithTheVersionsScale()
    {
        SegmentModel scaled = Model(
            new Dictionary<string, double> { [SegmentFeature.Length] = 2.0, [SegmentFeature.CaptionShare] = -1.0 },
            new Dictionary<string, FeatureScale>
            {
                [SegmentFeature.Length] = new(30, 15),
                [SegmentFeature.CaptionShare] = new(0.5, 0.25),
            });

        double score = scaled.BreakScore(Features((SegmentFeature.Length, 60), (SegmentFeature.CaptionShare, 0)), Station, null);
        double unknown = scaled.BreakScore(Features((SegmentFeature.Length, 60), (SegmentFeature.CaptionShare, null)), Station, null);

        Assert.Equal(-1.0 + (2.0 * 2) + (-1.0 * -2), score, 12);
        Assert.Equal(-1.0 + (2.0 * 2), unknown, 12);
    }

    [Fact(DisplayName = "the offsets of the station and of the series are added to the score, and a station or series without one adds nothing")]
    public void TheOffsetsAreAdded()
    {
        SegmentModel offset = new(
            SegmentModel.First.Weights,
            new Dictionary<string, double> { [Station] = 0.3 },
            new Dictionary<string, double> { ["架空の物語"] = -0.2 },
            SegmentThresholds.Initial,
            SegmentModel.First.Standardisation);
        string elsewhere = SegmentModel.StationKey(new NetworkId(4), new ServiceId(101));

        Assert.Equal(-2.5 + 0.3 - 0.2, offset.BreakScore(Features(), Station, "架空の物語"), 12);
        Assert.Equal(-2.5 + 0.3, offset.BreakScore(Features(), Station, null), 12);
        Assert.Equal(-2.5, offset.BreakScore(Features(), elsewhere, "別の架空の話"), 12);
    }

    [Fact(DisplayName = "a station is keyed by its network and service ids joined by a colon")]
    public void AStationIsKeyedByItsIds()
        => Assert.Equal("32736:1024", Station);

    [Fact(DisplayName = "weights naming a feature the detection does not compute, or no scale for a feature they weigh, are refused")]
    public void WeightsMustNameKnownFeaturesWithAScale()
    {
        Assert.Throws<ArgumentException>(() => new SegmentClassWeights(0, new Dictionary<string, double> { ["loudnessOfTheMoon"] = 1 }));
        Assert.Throws<ArgumentException>(() => Model(
            new Dictionary<string, double> { [SegmentFeature.Length] = 1 },
            new Dictionary<string, FeatureScale>()));
        Assert.Throws<ArgumentException>(() => Model(
            new Dictionary<string, double> { [SegmentFeature.Length] = 1 },
            new Dictionary<string, FeatureScale> { [SegmentFeature.Length] = FeatureScale.Identity, ["loudnessOfTheMoon"] = FeatureScale.Identity }));
    }

    [Fact(DisplayName = "the two-kind model weighs breaks against the programme and nothing else")]
    public void OnlyBreaksAreWeighed()
    {
        SegmentClassWeights some = new(0, new Dictionary<string, double>());

        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>()));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Programme] = some }));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Break] = some, [SegmentKind.Opening] = some }));
    }

    [Fact(DisplayName = "a number that is not finite, a scale of no spread and a malformed station key are refused")]
    public void BrokenNumbersAndKeysAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentClassWeights(double.NaN, new Dictionary<string, double>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentClassWeights(0, new Dictionary<string, double> { [SegmentFeature.Length] = double.PositiveInfinity }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureScale(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureScale(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureScale(double.NaN, 1));
        Assert.Throws<ArgumentException>(() => Offsets(new Dictionary<string, double> { ["32736-1024"] = 0.1 }));
        Assert.Throws<ArgumentException>(() => Offsets(new Dictionary<string, double> { ["70000:1"] = 0.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Offsets(new Dictionary<string, double> { [Station] = double.NaN }));
    }

    [Theory(DisplayName = "thresholds outside what they can mean are refused")]
    [InlineData(0, 0.1, 0.3, 1, -3, 1, 10, 10)]
    [InlineData(-130, 0.1, 0.3, 1, -3, 1, 10, 10)]
    [InlineData(-50, 0, 0.3, 1, -3, 1, 10, 10)]
    [InlineData(-50, 1.01, 0.3, 1, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, 0, 1, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, 1.01, 1, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, double.NaN, 1, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, 0.3, -1, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, 0.3, 7.5, -3, 1, 10, 10)]
    [InlineData(-50, 0.1, 0.3, 1, 0.5, 1, 10, 10)]
    [InlineData(-50, 0.1, 0.3, 1, -3, -1, 10, 10)]
    [InlineData(-50, 0.1, 0.3, 1, -3, 1, -10, 10)]
    [InlineData(-50, 0.1, 0.3, 1, -3, 1, 10, double.PositiveInfinity)]
    public void ThresholdsOutOfRangeAreRefused(
        double silenceDbfs,
        double blackLevel,
        double sceneChange,
        double gridToleranceSec,
        double gridPenalty,
        double lambda,
        double lambdaStation,
        double lambdaSeries)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new SegmentThresholds(silenceDbfs, blackLevel, sceneChange, gridToleranceSec, gridPenalty, lambda, lambdaStation, lambdaSeries));

    [Fact(DisplayName = "features are only the ones the detection computes, each finite or not known")]
    public void FeatureValuesAreKnownAndFinite()
    {
        Assert.Throws<ArgumentException>(() => Features(("loudnessOfTheMoon", 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Features((SegmentFeature.Length, double.NaN)));
        Assert.Null(Features()[SegmentFeature.Length]);
        Assert.Contains(SegmentFeature.Genre(7), SegmentFeature.Names);
        Assert.Equal(SegmentFeature.Names.Count, SegmentFeature.Names.Distinct(StringComparer.Ordinal).Count());
    }

    private static SegmentModel Model(IReadOnlyDictionary<string, double> features, IReadOnlyDictionary<string, FeatureScale> scales)
        => new(
            new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Break] = new(-1.0, features) }),
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            SegmentThresholds.Initial,
            scales);

    private static SegmentModel Offsets(IReadOnlyDictionary<string, double> stations)
        => new(SegmentModel.First.Weights, stations, new Dictionary<string, double>(), SegmentThresholds.Initial, SegmentModel.First.Standardisation);

    private static SegmentFeatureValues Features(params (string Name, double? Value)[] values)
        => SegmentFeatureValues.Of(values.ToDictionary(value => value.Name, value => value.Value));
}
