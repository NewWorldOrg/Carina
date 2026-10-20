using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Carina.Domain.Channels;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentModelTests
{
    private const string FirstWeights =
        """{ "classes": { "Break": { "bias": -2.5, "features": { "isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0 } } } }""";

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
        double plain = first.BreakScore(Features(), Station, null);

        Assert.Equal(1.0, pod, 12);
        Assert.Equal(-2.0, branded, 12);
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

    [Fact(DisplayName = "the two-kind model weighs breaks against the programme, the four-kind one the break, the opening and the ending each, and nothing else is weighed")]
    public void TheKindsWeighedAreTwoOrFour()
    {
        SegmentClassWeights some = new(0, new Dictionary<string, double>());

        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>()));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Programme] = some }));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Break] = some, [SegmentKind.Opening] = some }));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Opening] = some, [SegmentKind.Ending] = some }));
        Assert.Throws<ArgumentException>(() => new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Programme] = some, [SegmentKind.Break] = some, [SegmentKind.Opening] = some, [SegmentKind.Ending] = some }));
        Assert.Equal(
            [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending],
            new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights> { [SegmentKind.Ending] = some, [SegmentKind.Opening] = some, [SegmentKind.Break] = some }).Classes.Keys);
    }

    [Fact(DisplayName = "BR-CD-007: the first version of four kinds weighs the break as the first version does, and the opening and the ending by where earlier episodes had theirs, the catalog weighing nothing")]
    public void TheFirstVersionOfFourKindsIsHandSet()
    {
        SegmentModel four = SegmentModel.FirstOfFourKinds;

        Assert.Equal(4, four.Kinds);
        Assert.Equal(SegmentModel.First.Weights.Classes[SegmentKind.Break], four.Weights.Classes[SegmentKind.Break]);
        Assert.Equal(
            (-6.0, 4.0),
            (four.Weights.Classes[SegmentKind.Opening].Bias, four.Weights.Classes[SegmentKind.Opening].Features[SegmentFeature.PastOpeningShare]));
        Assert.Equal(
            (-6.0, 4.0),
            (four.Weights.Classes[SegmentKind.Ending].Bias, four.Weights.Classes[SegmentKind.Ending].Features[SegmentFeature.PastEndingShare]));
        Assert.Single(four.Weights.Classes[SegmentKind.Opening].Features);
        Assert.Single(four.Weights.Classes[SegmentKind.Ending].Features);
        Assert.Empty(four.StationOffsets);
        Assert.Empty(four.SeriesOffsets);
        Assert.Equal(SegmentThresholds.Initial, four.Thresholds);
        Assert.Equal(5, four.Standardisation.Count);
        Assert.All(four.Standardisation.Values, scale => Assert.Equal(FeatureScale.Identity, scale));
    }

    [Fact(DisplayName = "with the break alone weighed, the chances are the logistic function of its score to the last bit, and an opening or an ending is not likely at all")]
    public void TwoKindsGiveTheLogisticFunction()
    {
        foreach (double score in new[] { -800.0, -40, -2.5, -0.3, 0, 0.7, 3, 40, 800 })
        {
            SegmentChances chances = SegmentChances.OfABreak(score);

            Assert.Equal(SegmentModel.Chance(score), chances.Of(SegmentKind.Break));
            Assert.Equal(1 - SegmentModel.Chance(score), chances.Of(SegmentKind.Programme));
            Assert.Equal((0, 0), (chances.Of(SegmentKind.Opening), chances.Of(SegmentKind.Ending)));
            Assert.Equal(double.NegativeInfinity, chances.LogOf(SegmentKind.Opening));
            Assert.Equal(double.NegativeInfinity, chances.LogOf(SegmentKind.Ending));
            Assert.True(double.IsFinite(chances.LogOf(SegmentKind.Break)) && double.IsFinite(chances.LogOf(SegmentKind.Programme)));
            Assert.Equal(score >= 0 ? -Math.Log(1 + Math.Exp(-score)) : score - Math.Log(1 + Math.Exp(score)), chances.LogOf(SegmentKind.Break));
            Assert.Equal(score <= 0 ? Math.Log(1 + Math.Exp(score)) * -1 : -score - Math.Log(1 + Math.Exp(-score)), chances.LogOf(SegmentKind.Programme));
        }
    }

    [Fact(DisplayName = "four kinds are told apart by the softmax of their scores against the programme, which scores nothing, and the chances add up to one")]
    public void FourKindsGiveTheSoftmax()
    {
        SegmentChances chances = SegmentChances.Of(new Dictionary<SegmentKind, double>
        {
            [SegmentKind.Break] = 1.0,
            [SegmentKind.Opening] = 2.0,
            [SegmentKind.Ending] = -1.0,
        });
        double sum = 1 + Math.Exp(1) + Math.Exp(2) + Math.Exp(-1);

        Assert.Equal(1 / sum, chances.Of(SegmentKind.Programme), 12);
        Assert.Equal(Math.Exp(1) / sum, chances.Of(SegmentKind.Break), 12);
        Assert.Equal(Math.Exp(2) / sum, chances.Of(SegmentKind.Opening), 12);
        Assert.Equal(Math.Exp(-1) / sum, chances.Of(SegmentKind.Ending), 12);
        Assert.Equal(Math.Log(Math.Exp(2) / sum), chances.LogOf(SegmentKind.Opening), 12);
        Assert.Equal(1, Enum.GetValues<SegmentKind>().Sum(chances.Of), 12);
        Assert.Throws<ArgumentException>(() => SegmentChances.Of(new Dictionary<SegmentKind, double> { [SegmentKind.Programme] = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentChances.Of(new Dictionary<SegmentKind, double> { [SegmentKind.Break] = double.NaN }));
    }

    [Fact(DisplayName = "BR-CD-007: the first version of four kinds takes a piece where earlier episodes had their opening or their ending likelier for one, never likely enough on that alone, and a plain piece for the programme")]
    public void TheFirstVersionOfFourKindsLeavesOpeningsToTheCatalog()
    {
        SegmentModel four = SegmentModel.FirstOfFourKinds;

        SegmentChances remembered = four.Chances(Features((SegmentFeature.PastOpeningShare, 1), (SegmentFeature.LogoShare, 1)), Station, null);
        SegmentChances ending = four.Chances(Features((SegmentFeature.PastEndingShare, 1), (SegmentFeature.LogoShare, 1)), Station, null);
        SegmentChances plain = four.Chances(Features((SegmentFeature.LogoShare, 1)), Station, null);
        SegmentChances pod = four.Chances(Features((SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.LogoShare, 0)), Station, null);

        Assert.True(remembered.Of(SegmentKind.Opening) > plain.Of(SegmentKind.Opening));
        Assert.True(remembered.Of(SegmentKind.Opening) < remembered.Of(SegmentKind.Programme));
        Assert.True(ending.Of(SegmentKind.Ending) < ending.Of(SegmentKind.Programme));
        Assert.True(plain.Of(SegmentKind.Programme) > 0.9);
        Assert.True(pod.Of(SegmentKind.Break) > 0.5);
        Assert.Equal(1.0, four.BreakScore(Features((SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.LogoShare, 0)), Station, null), 12);
    }

    [Fact(DisplayName = "BR-CD-007: the version of four kinds for any series hears silence as the version hearing silence from -55 dBFS does and weighs the break as it does but for the grid, which counts only up to a minute, and weighs the opening and the ending by where they lie, how long they last, how steady their loudness is and the grid, an advert's length most, earlier episodes adding to it")]
    public void TheVersionForAnySeriesIsHandSet()
    {
        SegmentModel any = SegmentModel.FourKindsInAnySeries;

        Assert.Equal(4, any.Kinds);
        Assert.Equal(SegmentModel.FourKindsBelowPauses.Thresholds, any.Thresholds);
        Assert.Equal(
            new SegmentClassWeights(-2.5, new Dictionary<string, double>
            {
                [SegmentFeature.IsShortMultipleOf15s] = 2.0,
                [SegmentFeature.EdgeSilence] = 1.5,
                [SegmentFeature.LogoShare] = -3.0,
            }),
            any.Weights.Classes[SegmentKind.Break]);
        Assert.Equal(Theme(SegmentFeature.OpeningWindow, SegmentFeature.PastOpeningShare), any.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(Theme(SegmentFeature.EndingWindow, SegmentFeature.PastEndingShare), any.Weights.Classes[SegmentKind.Ending]);
        Assert.Equal(new FeatureScale(8, 1), any.Standardisation[SegmentFeature.LoudnessRange]);
        Assert.All(
            any.Standardisation.Where(pair => pair.Key is not SegmentFeature.LoudnessRange),
            pair => Assert.Equal(FeatureScale.Identity, pair.Value));
        Assert.Empty(any.StationOffsets);
        Assert.Empty(any.SeriesOffsets);
    }

    [Fact(DisplayName = "BR-CD-007: with neither the catalog nor earlier episodes, the version for any series takes a song as long as a whole one where an opening or an ending lies for one at 0.6 to 0.8, one of a minute and a half between two silent cuts likelier for one than anything else, and speech there, a pod of adverts there and a song in the middle of the programme for what they are")]
    public void TheVersionForAnySeriesFindsAThemeInAFirstEpisode()
    {
        SegmentModel any = SegmentModel.FourKindsInAnySeries;

        SegmentChances opening = any.Chances(Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 3)), Station, null);
        SegmentChances ending = any.Chances(Features((SegmentFeature.EndingWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 3)), Station, null);
        SegmentChances onTheGrid = any.Chances(
            Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 3), (SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1)),
            Station,
            null);
        SegmentChances speech = any.Chances(Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 15)), Station, null);
        SegmentChances pod = any.Chances(
            Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.IsShortMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.LoudnessRange, 2)),
            Station,
            null);
        SegmentChances middle = any.Chances(Features((SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 3)), Station, null);

        Assert.InRange(opening.Of(SegmentKind.Opening), 0.6, 0.8);
        Assert.InRange(ending.Of(SegmentKind.Ending), 0.6, 0.8);
        Assert.True(onTheGrid.Of(SegmentKind.Opening) > Math.Max(onTheGrid.Of(SegmentKind.Programme), onTheGrid.Of(SegmentKind.Break)));
        Assert.True(speech.Of(SegmentKind.Programme) > 0.85);
        Assert.True(pod.Of(SegmentKind.Break) > 0.5);
        Assert.True(pod.Of(SegmentKind.Opening) < 0.1);
        Assert.True(middle.Of(SegmentKind.Programme) > 0.85);
    }

    [Fact(DisplayName = "BR-CD-007: the version with songs whose loudness moves weighs as the version for any series but centres the loudness range on 10 dB and weighs an advert's length 0.8 more against the opening and the ending, so a song of 7 dB where an ending lies is the ending and an advert there is taken as before")]
    public void TheVersionWithDynamicSongsIsHandSet()
    {
        SegmentModel any = SegmentModel.FourKindsInAnySeries;
        SegmentModel dynamic = SegmentModel.FourKindsWithDynamicSongs;
        SegmentFeatureValues song = Features((SegmentFeature.EndingWindow, 1), (SegmentFeature.LoudnessRange, 7));
        SegmentFeatureValues advert = Features(
            (SegmentFeature.EndingWindow, 1),
            (SegmentFeature.IsMultipleOf15s, 1),
            (SegmentFeature.IsShortMultipleOf15s, 1),
            (SegmentFeature.EdgeSilence, 1),
            (SegmentFeature.LoudnessRange, 3));

        Assert.Equal(any.Weights.Classes[SegmentKind.Break], dynamic.Weights.Classes[SegmentKind.Break]);
        Assert.Equal(any.Thresholds, dynamic.Thresholds);
        Assert.Equal(new FeatureScale(10, 1), dynamic.Standardisation[SegmentFeature.LoudnessRange]);
        Assert.All(
            [SegmentKind.Opening, SegmentKind.Ending],
            kind => Assert.Equal((-2.8, -0.4), (dynamic.Weights.Classes[kind].Features[SegmentFeature.IsShortMultipleOf15s], dynamic.Weights.Classes[kind].Features[SegmentFeature.LoudnessRange])));
        Assert.True(any.Chances(song, Station, null).Of(SegmentKind.Programme) > any.Chances(song, Station, null).Of(SegmentKind.Ending));
        Assert.True(dynamic.Chances(song, Station, null).Of(SegmentKind.Ending) > dynamic.Chances(song, Station, null).Of(SegmentKind.Programme));
        Assert.Equal(any.Chances(advert, Station, null).Of(SegmentKind.Break), dynamic.Chances(advert, Station, null).Of(SegmentKind.Break), 12);
    }

    [Fact(DisplayName = "BR-CD-007: the version for pods weighs as the version with songs whose loudness moves but adds 1.5 to an advert with another beside it and no longer weighs a multiple of the grid against the opening and the ending, so an advert of a pod is a break at more than 0.9, a lone one as before, and a whole opening of 90 seconds on the grid as likely as one off it")]
    public void TheVersionForPodsIsHandSet()
    {
        SegmentModel dynamic = SegmentModel.FourKindsWithDynamicSongs;
        SegmentModel pods = SegmentModel.FourKindsInPods;
        SegmentFeatureValues lone = Features((SegmentFeature.IsShortMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1));
        SegmentFeatureValues beside = Features((SegmentFeature.IsShortMultipleOf15s, 1), (SegmentFeature.EdgeSilence, 1), (SegmentFeature.AdvertBeside, 1));
        SegmentFeatureValues onTheGrid = Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.IsMultipleOf15s, 1), (SegmentFeature.LoudnessRange, 4));
        SegmentFeatureValues offTheGrid = Features((SegmentFeature.OpeningWindow, 1), (SegmentFeature.IsThemeLength, 1), (SegmentFeature.LoudnessRange, 4));

        Assert.Equal(dynamic.Thresholds, pods.Thresholds);
        Assert.Equal(1.5, pods.Weights.Classes[SegmentKind.Break].Features[SegmentFeature.AdvertBeside]);
        Assert.All([SegmentKind.Opening, SegmentKind.Ending], kind => Assert.False(pods.Weights.Classes[kind].Features.ContainsKey(SegmentFeature.IsMultipleOf15s)));
        Assert.Equal(dynamic.Chances(lone, Station, null).Of(SegmentKind.Break), pods.Chances(lone, Station, null).Of(SegmentKind.Break), 12);
        Assert.True(pods.Chances(beside, Station, null).Of(SegmentKind.Break) > 0.9);
        Assert.Equal(pods.Chances(offTheGrid, Station, null).Of(SegmentKind.Opening), pods.Chances(onTheGrid, Station, null).Of(SegmentKind.Opening), 12);
        Assert.True(dynamic.Chances(onTheGrid, Station, null).Of(SegmentKind.Opening) < pods.Chances(onTheGrid, Station, null).Of(SegmentKind.Opening));
    }

    [Fact(DisplayName = "the offsets of the station and of the series are added to the score of a break alone, so the opening and the ending score by their own weights")]
    public void TheOffsetsWeighTheBreakAlone()
    {
        SegmentModel offset = new(
            SegmentModel.FirstOfFourKinds.Weights,
            new Dictionary<string, double> { [Station] = 0.3 },
            new Dictionary<string, double> { ["架空の物語"] = -0.2 },
            SegmentThresholds.Initial,
            SegmentModel.FirstOfFourKinds.Standardisation);

        Assert.Equal(-2.5 + 0.3 - 0.2, offset.Score(SegmentKind.Break, Features(), Station, "架空の物語"), 12);
        Assert.Equal(-6.0, offset.Score(SegmentKind.Opening, Features(), Station, "架空の物語"), 12);
        Assert.Equal(-6.0, offset.Score(SegmentKind.Ending, Features(), Station, null), 12);
        Assert.Equal(0, offset.Score(SegmentKind.Programme, Features(), Station, "架空の物語"));
        Assert.Throws<ArgumentException>(() => SegmentModel.First.Score(SegmentKind.Opening, Features(), Station, null));
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

    private static SegmentClassWeights Theme(string place, string past)
        => new(-6.0, new Dictionary<string, double>
        {
            [place] = 4.8,
            [SegmentFeature.IsThemeLength] = 0.5,
            [SegmentFeature.LoudnessRange] = -0.4,
            [SegmentFeature.IsMultipleOf15s] = -1.0,
            [SegmentFeature.IsShortMultipleOf15s] = -2.0,
            [past] = 4.0,
        });

    private static SegmentFeatureValues Features(params (string Name, double? Value)[] values)
        => SegmentFeatureValues.Of(values.ToDictionary(value => value.Name, value => value.Value));
}
