using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentRegressionTests
{
    private const string Station = "1:1";

    private const string OtherStation = "4:101";

    [Fact(DisplayName = "with nothing but the labels to go on, the intercept is the log of the weighted odds of a break, and nothing else moves")]
    public void TheInterceptAloneIsTheWeightedLogOdds()
    {
        SegmentSample[] samples =
        [
            Sample(true, 1.0),
            Sample(true, 1.0),
            Sample(true, 1.0),
            Sample(false, 0.5),
            Sample(false, 0.5),
            Sample(false, 1.0),
        ];

        SegmentFit fit = SegmentRegression.Fit(samples, SegmentThresholds.Initial);

        Assert.True(fit.Converged);
        Assert.Equal(Math.Log(3.0 / 2.0), fit.Model.Weights.Classes[SegmentKind.Break].Bias, 5);
        Assert.All(fit.Model.Weights.Classes[SegmentKind.Break].Features.Values, weight => Assert.Equal(0, weight));
        Assert.Equal(0, fit.Model.StationOffsets[Station], 5);
        Assert.Equal(0.6, SegmentModel.Chance(fit.Model.BreakScore(samples[0].Features, Station, null)), 5);
    }

    [Fact(DisplayName = "a set one feature separates converges under the pull towards zero, to finite weights that tell every sample apart at a standstill of the penalised likelihood")]
    public void ASeparableSetConvergesUnderThePull()
    {
        SegmentSample[] samples =
        [
            .. new[] { 14.5, 15.0, 15.2, 29.8, 30.0 }.Select(seconds => Sample(true, 1.0, seconds)),
            .. new[] { 60.0, 90.0, 120.0, 150.0, 180.0 }.Select(seconds => Sample(false, 1.0, seconds)),
        ];
        SegmentThresholds thresholds = Pulled(0.1, 10, 10);

        SegmentFit fit = SegmentRegression.Fit(samples, thresholds);
        SegmentClassWeights weights = fit.Model.Weights.Classes[SegmentKind.Break];
        FeatureScale scale = fit.Model.Standardisation[SegmentFeature.Length];
        double[] chances = [.. samples.Select(sample => SegmentModel.Chance(fit.Model.BreakScore(sample.Features, Station, null)))];
        double slope = samples.Sum(sample => sample.Weight * (Label(sample) - Chance(fit, sample)) * scale.Standardised(sample.Features[SegmentFeature.Length]!.Value));

        Assert.True(fit.Converged);
        Assert.InRange(fit.Iterations, 1, SegmentRegression.MostIterations - 1);
        Assert.True(double.IsFinite(weights.Features[SegmentFeature.Length]));
        Assert.True(weights.Features[SegmentFeature.Length] < 0);
        Assert.All(chances.Take(5), chance => Assert.True(chance > 0.5));
        Assert.All(chances.Skip(5), chance => Assert.True(chance < 0.5));
        Assert.Equal(2 * thresholds.Lambda * weights.Features[SegmentFeature.Length], slope, 4);
        Assert.Equal(0, samples.Sum(sample => sample.Weight * (Label(sample) - Chance(fit, sample))), 4);
    }

    [Fact(DisplayName = "each feature is standardised by the mean and standard deviation of the samples it is known in, and one that never varies keeps a deviation of one")]
    public void TheScalesComeFromTheSamples()
    {
        SegmentSample[] samples = [Sample(true, 1.0, 10, 0.5), Sample(false, 1.0, 20, 0.5), Sample(false, 1.0, 30, null), Sample(true, 0.2, 60, 0.5)];

        SegmentModel model = SegmentRegression.Fit(samples, SegmentThresholds.Initial).Model;

        Assert.Equal(30, model.Standardisation[SegmentFeature.Length].Mean, 12);
        Assert.Equal(Math.Sqrt(350), model.Standardisation[SegmentFeature.Length].Sd, 12);
        Assert.Equal(new FeatureScale(0.5, 1), model.Standardisation[SegmentFeature.LogoShare]);
        Assert.Equal(new FeatureScale(0, 1), model.Standardisation[SegmentFeature.CaptionShare]);
        Assert.Equal(SegmentFeature.Names.Count, model.Standardisation.Count);
    }

    [Fact(DisplayName = "an offset is learned per station and per series, a stronger pull shrinks it towards zero, and a station or series with no samples has none")]
    public void OffsetsAreLearnedAndShrunk()
    {
        SegmentSample[] samples =
        [
            .. Enumerable.Range(0, 40).Select(at => Sample(at % 5 is not 0, 1.0, station: Station, series: "架空の物語")),
            .. Enumerable.Range(0, 40).Select(at => Sample(at % 5 is 0, 1.0, station: OtherStation)),
        ];

        SegmentModel loose = SegmentRegression.Fit(samples, Pulled(1, 1, 1)).Model;
        SegmentModel tight = SegmentRegression.Fit(samples, Pulled(1, 100, 100)).Model;

        Assert.True(loose.StationOffsets[Station] > loose.StationOffsets[OtherStation]);
        Assert.True(loose.SeriesOffsets["架空の物語"] > 0);
        Assert.True(Math.Abs(tight.StationOffsets[Station]) < Math.Abs(loose.StationOffsets[Station]));
        Assert.True(Math.Abs(tight.SeriesOffsets["架空の物語"]) < Math.Abs(loose.SeriesOffsets["架空の物語"]));
        Assert.Equal([Station, OtherStation], loose.StationOffsets.Keys);
        Assert.Equal(["架空の物語"], loose.SeriesOffsets.Keys);
        Assert.Equal(Pulled(1, 100, 100), tight.Thresholds);
    }

    [Fact(DisplayName = "the same samples give an identical model every time, and starting from another model reaches the same one")]
    public void TheSameSamplesGiveTheSameModel()
    {
        SegmentSample[] samples =
        [
            .. Enumerable.Range(0, 30).Select(at => Sample(at % 3 is 0, 0.2 + (at % 4 * 0.2), 5 + (at * 7 % 90), at % 7 / 7.0, at % 2 is 0 ? Station : OtherStation, at % 3 is 1 ? "架空の物語" : null)),
        ];

        SegmentFit once = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentFit again = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentFit started = SegmentRegression.Fit(samples, SegmentThresholds.Initial, SegmentModel.First);

        Assert.Equal(once.Model, again.Model);
        Assert.Equal(once.Iterations, again.Iterations);
        Assert.Equal(once.Model.Weights.Classes[SegmentKind.Break].Bias, started.Model.Weights.Classes[SegmentKind.Break].Bias, 4);
        Assert.Equal(once.Model.Weights.Classes[SegmentKind.Break].Features[SegmentFeature.Length], started.Model.Weights.Classes[SegmentKind.Break].Features[SegmentFeature.Length], 4);
    }

    [Fact(DisplayName = "BR-CD-007: labels of programme and breaks alone are fitted as two kinds, and an opening or an ending among them makes the fit tell four kinds apart")]
    public void TheKindsFittedAreTheKindsLabelled()
    {
        SegmentSample[] twoKinds = [Sample(true, 1.0, 15), Sample(false, 1.0, 300)];

        Assert.Equal(2, SegmentRegression.Fit(twoKinds, SegmentThresholds.Initial).Model.Kinds);
        Assert.Equal(4, SegmentRegression.Fit([.. twoKinds, Sample(SegmentKind.Opening, 1.0, 90)], SegmentThresholds.Initial).Model.Kinds);
        Assert.Equal(4, SegmentRegression.Fit([.. twoKinds, Sample(SegmentKind.Ending, 1.0, 90)], SegmentThresholds.Initial).Model.Kinds);
        Assert.Equal(2, SegmentRegression.Fit(twoKinds, SegmentThresholds.Initial, SegmentModel.FirstOfFourKinds).Model.Kinds);
    }

    [Fact(DisplayName = "with nothing but the labels to go on, each kind's intercept is the log of its weighted odds against the programme")]
    public void TheInterceptsAloneAreTheWeightedLogOddsOfEachKind()
    {
        SegmentSample[] samples =
        [
            Sample(SegmentKind.Programme, 1.0),
            Sample(SegmentKind.Programme, 1.0),
            Sample(SegmentKind.Programme, 0.5),
            Sample(SegmentKind.Programme, 0.5),
            Sample(SegmentKind.Break, 1.0),
            Sample(SegmentKind.Break, 0.5),
            Sample(SegmentKind.Opening, 0.6),
            Sample(SegmentKind.Ending, 0.2),
            Sample(SegmentKind.Ending, 0.2),
        ];

        SegmentFit fit = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentChances chances = fit.Model.Chances(samples[0].Features, Station, null);

        Assert.True(fit.Converged);
        Assert.Equal(Math.Log(1.5 / 3), fit.Model.Weights.Classes[SegmentKind.Break].Bias, 5);
        Assert.Equal(Math.Log(0.6 / 3), fit.Model.Weights.Classes[SegmentKind.Opening].Bias, 5);
        Assert.Equal(Math.Log(0.4 / 3), fit.Model.Weights.Classes[SegmentKind.Ending].Bias, 5);
        Assert.All(fit.Model.Weights.Classes.Values.SelectMany(weights => weights.Features.Values), weight => Assert.Equal(0, weight));
        Assert.Equal(3 / 5.5, chances.Of(SegmentKind.Programme), 5);
        Assert.Equal(0.6 / 5.5, chances.Of(SegmentKind.Opening), 5);
    }

    [Fact(DisplayName = "a four-kind fit comes to a standstill of the penalised likelihood: for every kind the weighted residuals sum to nothing, and the slope along each feature is its pull")]
    public void AFourKindFitComesToAStandstill()
    {
        SegmentSample[] samples =
        [
            .. new[] { 14.5, 15.0, 29.8, 30.0, 45.2 }.Select(seconds => Sample(SegmentKind.Break, 0.9, seconds)),
            .. new[] { 85.0, 89.5, 90.0, 91.0 }.Select(seconds => Sample(SegmentKind.Opening, 0.8, seconds, 0.2)),
            .. new[] { 60.0, 88.0, 90.0 }.Select(seconds => Sample(SegmentKind.Ending, 0.8, seconds, 0.9)),
            .. new[] { 120.0, 200.0, 300.0, 90.0, 400.0, 31.0 }.Select(seconds => Sample(SegmentKind.Programme, 1.0, seconds, 0.6)),
        ];
        SegmentThresholds thresholds = Pulled(0.5, 10, 10);

        SegmentFit fit = SegmentRegression.Fit(samples, thresholds);

        Assert.True(fit.Converged);
        Assert.InRange(fit.Iterations, 1, SegmentRegression.MostIterations - 1);

        foreach (SegmentKind kind in new[] { SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending })
        {
            SegmentClassWeights weights = fit.Model.Weights.Classes[kind];
            double[] residuals = [.. samples.Select(sample => sample.Weight * ((sample.Kind == kind ? 1 : 0) - fit.Model.Chances(sample.Features, sample.Station, sample.Series).Of(kind)))];

            foreach (string feature in new[] { SegmentFeature.Length, SegmentFeature.LogoShare })
            {
                FeatureScale scale = fit.Model.Standardisation[feature];
                double slope = samples.Select((sample, at) => residuals[at] * (sample.Features[feature] is double value ? scale.Standardised(value) : 0)).Sum();

                Assert.Equal(2 * thresholds.Lambda * weights.Features[feature], slope, 4);
            }

            Assert.Equal(0, residuals.Sum(), 4);
        }

        Assert.True(Chance(fit, samples[5], SegmentKind.Opening) > Chance(fit, samples[5], SegmentKind.Break));
        Assert.True(Chance(fit, samples[0], SegmentKind.Break) > Chance(fit, samples[0], SegmentKind.Opening));
    }

    [Fact(DisplayName = "BR-CD-007: the offsets of a station and a series weigh the break alone, so a station with more breaks has the larger one whatever its openings and endings")]
    public void FourKindsLearnTheOffsetsOfBreaks()
    {
        SegmentKind[] busy = [SegmentKind.Break, SegmentKind.Break, SegmentKind.Break, SegmentKind.Programme, SegmentKind.Opening, SegmentKind.Ending];
        SegmentKind[] quiet = [SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending];
        SegmentSample[] samples =
        [
            .. Enumerable.Range(0, 42).Select(at => Sample(busy[at % busy.Length], 1.0, station: Station)),
            .. Enumerable.Range(0, 42).Select(at => Sample(quiet[at % quiet.Length], 1.0, station: OtherStation)),
        ];

        SegmentModel model = SegmentRegression.Fit(samples, Pulled(1, 1, 1)).Model;
        SegmentFeatureValues none = samples[0].Features;

        Assert.Equal(4, model.Kinds);
        Assert.True(model.StationOffsets[Station] > model.StationOffsets[OtherStation]);
        Assert.Equal(model.Score(SegmentKind.Opening, none, Station, null), model.Score(SegmentKind.Opening, none, OtherStation, null));
        Assert.Equal([Station, OtherStation], model.StationOffsets.Keys);
    }

    [Fact(DisplayName = "A-051: with the opening and the ending kept from a version that weighs them, the fit weighs the break as the two-kind fit does and hands the kept weights and their scales on unchanged")]
    public void TheOpeningAndTheEndingAreKeptUnchanged()
    {
        SegmentSample[] samples =
        [
            .. Enumerable.Range(0, 30).Select(at => Sample(at % 3 is 0, 0.2 + (at % 4 * 0.2), 5 + (at * 7 % 90), at % 7 / 7.0, at % 2 is 0 ? Station : OtherStation, at % 3 is 1 ? "架空の物語" : null)),
        ];
        SegmentModel bare = Kept(new Dictionary<string, double>(), new Dictionary<string, FeatureScale>());
        SegmentModel weighing = Kept(
            new Dictionary<string, double> { [SegmentFeature.Length] = 0.5, [SegmentFeature.CatalogOpeningShare] = 8 },
            new Dictionary<string, FeatureScale> { [SegmentFeature.Length] = new(60, 30), [SegmentFeature.CatalogOpeningShare] = new(0.2, 0.5) });

        SegmentFit two = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentFit keptBare = SegmentRegression.Fit(samples, SegmentThresholds.Initial, kept: bare);
        SegmentFit keptWeighing = SegmentRegression.Fit(samples, SegmentThresholds.Initial, two.Model, weighing);

        Assert.Equal(4, keptBare.Model.Kinds);
        Assert.Equal(two.Model.Weights.Classes[SegmentKind.Break], keptBare.Model.Weights.Classes[SegmentKind.Break]);
        Assert.True(SortedMapsEqual(two.Model.Standardisation, keptBare.Model.Standardisation));
        Assert.Equal(bare.Weights.Classes[SegmentKind.Opening], keptBare.Model.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(weighing.Weights.Classes[SegmentKind.Opening], keptWeighing.Model.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(weighing.Weights.Classes[SegmentKind.Ending], keptWeighing.Model.Weights.Classes[SegmentKind.Ending]);
        Assert.Equal(new FeatureScale(60, 30), keptWeighing.Model.Standardisation[SegmentFeature.Length]);
        Assert.Equal(new FeatureScale(0.2, 0.5), keptWeighing.Model.Standardisation[SegmentFeature.CatalogOpeningShare]);
        Assert.Equal(two.Model.Standardisation[SegmentFeature.LogoShare], keptWeighing.Model.Standardisation[SegmentFeature.LogoShare]);
        Assert.NotEqual(weighing.Weights.Classes[SegmentKind.Break], keptWeighing.Model.Weights.Classes[SegmentKind.Break]);
        Assert.True(keptWeighing.Converged);
    }

    [Theory(DisplayName = "A-051: of the opening and the ending, the one labelled is fitted and the one nothing labels is kept as it was, and nothing is kept from a version of two kinds")]
    [InlineData(SegmentKind.Opening, SegmentKind.Ending)]
    [InlineData(SegmentKind.Ending, SegmentKind.Opening)]
    public void TheKindNothingLabelsIsKept(SegmentKind labelled, SegmentKind unlabelled)
    {
        SegmentSample[] samples =
        [
            .. new[] { 14.5, 15.0, 29.8, 30.0 }.Select(seconds => Sample(SegmentKind.Break, 0.9, seconds)),
            .. new[] { 85.0, 89.5, 90.0 }.Select(seconds => Sample(labelled, 0.8, seconds, 0.2)),
            .. new[] { 120.0, 200.0, 300.0, 31.0 }.Select(seconds => Sample(SegmentKind.Programme, 1.0, seconds, 0.6)),
        ];
        SegmentModel kept = SegmentModel.FirstOfFourKinds;

        SegmentModel fitted = SegmentRegression.Fit(samples, SegmentThresholds.Initial, kept: kept).Model;
        SegmentModel alone = SegmentRegression.Fit(samples, SegmentThresholds.Initial).Model;

        Assert.Equal(4, fitted.Kinds);
        Assert.Equal(kept.Weights.Classes[unlabelled], fitted.Weights.Classes[unlabelled]);
        Assert.NotEqual(kept.Weights.Classes[labelled], fitted.Weights.Classes[labelled]);
        Assert.True(alone.Weights.Classes[unlabelled].Bias < -10);
        Assert.Throws<ArgumentException>(() => SegmentRegression.Fit(samples, SegmentThresholds.Initial, kept: SegmentModel.First));
    }

    [Fact(DisplayName = "the same four-kind samples give an identical model every time, and starting from the first version of four kinds or of two reaches the same one")]
    public void TheSameFourKindSamplesGiveTheSameModel()
    {
        SegmentSample[] samples =
        [
            .. Enumerable.Range(0, 40).Select(at => Sample((SegmentKind)(1 + (at % 4)), 0.2 + (at % 4 * 0.2), 5 + (at * 7 % 90), at % 7 / 7.0, at % 2 is 0 ? Station : OtherStation, at % 3 is 1 ? "架空の物語" : null)),
        ];

        SegmentFit once = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentFit again = SegmentRegression.Fit(samples, SegmentThresholds.Initial);
        SegmentFit fromFour = SegmentRegression.Fit(samples, SegmentThresholds.Initial, SegmentModel.FirstOfFourKinds);
        SegmentFit fromTwo = SegmentRegression.Fit(samples, SegmentThresholds.Initial, SegmentModel.First);

        Assert.Equal(once.Model, again.Model);
        Assert.Equal(once.Iterations, again.Iterations);

        foreach (SegmentKind kind in new[] { SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending })
        {
            Assert.Equal(once.Model.Weights.Classes[kind].Bias, fromFour.Model.Weights.Classes[kind].Bias, 4);
            Assert.Equal(once.Model.Weights.Classes[kind].Features[SegmentFeature.Length], fromTwo.Model.Weights.Classes[kind].Features[SegmentFeature.Length], 4);
        }
    }

    [Fact(DisplayName = "no samples leave every parameter at zero")]
    public void NoSamplesLeaveEverythingAtZero()
    {
        SegmentFit fit = SegmentRegression.Fit([], SegmentThresholds.Initial);

        Assert.Equal(0, fit.Model.Weights.Classes[SegmentKind.Break].Bias);
        Assert.Empty(fit.Model.StationOffsets);
        Assert.All(fit.Model.Weights.Classes[SegmentKind.Break].Features.Values, weight => Assert.Equal(0, weight));
    }

    [Fact(DisplayName = "a sample is weighed above nothing, on a station, with a series named in some characters or none")]
    public void ASampleIsWeighed()
    {
        SegmentFeatureValues none = SegmentFeatureValues.Of(new Dictionary<string, double?>());

        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentSample(none, Station, null, SegmentKind.Break, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentSample(none, Station, null, SegmentKind.Break, double.NaN));
        Assert.Throws<ArgumentException>(() => new SegmentSample(none, "1", null, SegmentKind.Break, 1));
        Assert.Throws<ArgumentException>(() => new SegmentSample(none, Station, string.Empty, SegmentKind.Break, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentSample(none, Station, null, (SegmentKind)9, 1));
    }

    private static SegmentModel Kept(IReadOnlyDictionary<string, double> opening, IReadOnlyDictionary<string, FeatureScale> scales)
        => new(
            new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
            {
                [SegmentKind.Break] = new(-1.0, new Dictionary<string, double>()),
                [SegmentKind.Opening] = new(-6.0, opening),
                [SegmentKind.Ending] = new(-5.0, opening),
            }),
            new Dictionary<string, double>(),
            new Dictionary<string, double>(),
            SegmentThresholds.Initial,
            scales);

    private static bool SortedMapsEqual(IReadOnlyDictionary<string, FeatureScale> one, IReadOnlyDictionary<string, FeatureScale> other)
        => one.Count == other.Count && one.All(pair => other.TryGetValue(pair.Key, out FeatureScale? found) && found == pair.Value);

    private static double Label(SegmentSample sample) => sample.Kind is SegmentKind.Break ? 1 : 0;

    private static double Chance(SegmentFit fit, SegmentSample sample) => SegmentModel.Chance(fit.Model.BreakScore(sample.Features, sample.Station, sample.Series));

    private static double Chance(SegmentFit fit, SegmentSample sample, SegmentKind kind) => fit.Model.Chances(sample.Features, sample.Station, sample.Series).Of(kind);

    private static SegmentThresholds Pulled(double lambda, double station, double series)
        => new(-50, 0.10, 0.30, 1.0, -3.0, lambda, station, series);

    private static SegmentSample Sample(
        bool isBreak,
        double weight,
        double? length = null,
        double? logo = null,
        string station = Station,
        string? series = null)
        => Sample(isBreak ? SegmentKind.Break : SegmentKind.Programme, weight, length, logo, station, series);

    private static SegmentSample Sample(
        SegmentKind kind,
        double weight,
        double? length = null,
        double? logo = null,
        string station = Station,
        string? series = null)
    {
        Dictionary<string, double?> features = [];

        if (length is double seconds)
        {
            features[SegmentFeature.Length] = seconds;
        }

        if (logo is double share)
        {
            features[SegmentFeature.LogoShare] = share;
        }

        return new SegmentSample(SegmentFeatureValues.Of(features), station, series, kind, weight);
    }
}
