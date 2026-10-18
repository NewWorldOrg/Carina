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

        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentSample(none, Station, null, true, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SegmentSample(none, Station, null, true, double.NaN));
        Assert.Throws<ArgumentException>(() => new SegmentSample(none, "1", null, true, 1));
        Assert.Throws<ArgumentException>(() => new SegmentSample(none, Station, string.Empty, true, 1));
    }

    private static double Label(SegmentSample sample) => sample.IsBreak ? 1 : 0;

    private static double Chance(SegmentFit fit, SegmentSample sample) => SegmentModel.Chance(fit.Model.BreakScore(sample.Features, sample.Station, sample.Series));

    private static SegmentThresholds Pulled(double lambda, double station, double series)
        => new(-50, 0.10, 0.30, 1.0, -3.0, lambda, station, series);

    private static SegmentSample Sample(
        bool isBreak,
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

        return new SegmentSample(SegmentFeatureValues.Of(features), station, series, isBreak, weight);
    }
}
