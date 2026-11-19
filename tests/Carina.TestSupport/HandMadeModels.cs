using Carina.Domain.Segments;

namespace Carina.TestSupport;

public static class HandMadeModels
{
    public static SegmentModel First { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = new(-2.5, new Dictionary<string, double>
            {
                [SegmentFeature.IsMultipleOf15s] = 2.0,
                [SegmentFeature.EdgeSilence] = 1.5,
                [SegmentFeature.LogoShare] = -3.0,
            }),
        }),
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        SegmentThresholds.Initial,
        new Dictionary<string, FeatureScale>
        {
            [SegmentFeature.IsMultipleOf15s] = FeatureScale.Identity,
            [SegmentFeature.EdgeSilence] = FeatureScale.Identity,
            [SegmentFeature.LogoShare] = FeatureScale.Identity,
        });

    public static SegmentModel FirstOfFourKinds { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = First.Weights.Classes[SegmentKind.Break],
            [SegmentKind.Opening] = new(-6.0, new Dictionary<string, double>
            {
                [SegmentFeature.PastOpeningShare] = 4.0,
            }),
            [SegmentKind.Ending] = new(-6.0, new Dictionary<string, double>
            {
                [SegmentFeature.PastEndingShare] = 4.0,
            }),
        }),
        new Dictionary<string, double>(),
        new Dictionary<string, double>(),
        SegmentThresholds.Initial,
        new Dictionary<string, FeatureScale>
        {
            [SegmentFeature.IsMultipleOf15s] = FeatureScale.Identity,
            [SegmentFeature.EdgeSilence] = FeatureScale.Identity,
            [SegmentFeature.LogoShare] = FeatureScale.Identity,
            [SegmentFeature.PastOpeningShare] = FeatureScale.Identity,
            [SegmentFeature.PastEndingShare] = FeatureScale.Identity,
        });

    public static SegmentModel FourKindsBelowPauses { get; } = new(
        FirstOfFourKinds.Weights,
        FirstOfFourKinds.StationOffsets,
        FirstOfFourKinds.SeriesOffsets,
        new SegmentThresholds(
            -55,
            SegmentThresholds.Initial.BlackLevel,
            SegmentThresholds.Initial.SceneChange,
            SegmentThresholds.Initial.GridToleranceSec,
            SegmentThresholds.Initial.GridPenalty,
            SegmentThresholds.Initial.Lambda,
            SegmentThresholds.Initial.LambdaStation,
            SegmentThresholds.Initial.LambdaSeries),
        FirstOfFourKinds.Standardisation);

    public static SegmentModel FourKindsInAnySeries { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = new(-2.5, new Dictionary<string, double>
            {
                [SegmentFeature.IsShortMultipleOf15s] = 2.0,
                [SegmentFeature.EdgeSilence] = 1.5,
                [SegmentFeature.LogoShare] = -3.0,
            }),
            [SegmentKind.Opening] = ThemeWeights(SegmentFeature.OpeningWindow, SegmentFeature.PastOpeningShare),
            [SegmentKind.Ending] = ThemeWeights(SegmentFeature.EndingWindow, SegmentFeature.PastEndingShare),
        }),
        FourKindsBelowPauses.StationOffsets,
        FourKindsBelowPauses.SeriesOffsets,
        FourKindsBelowPauses.Thresholds,
        new Dictionary<string, FeatureScale>(FourKindsBelowPauses.Standardisation)
        {
            [SegmentFeature.OpeningWindow] = FeatureScale.Identity,
            [SegmentFeature.EndingWindow] = FeatureScale.Identity,
            [SegmentFeature.IsThemeLength] = FeatureScale.Identity,
            [SegmentFeature.LoudnessRange] = new(8, 1),
            [SegmentFeature.IsShortMultipleOf15s] = FeatureScale.Identity,
        });

    public static SegmentModel FourKindsWithDynamicSongs { get; } = new(
        new SegmentWeights(new Dictionary<SegmentKind, SegmentClassWeights>
        {
            [SegmentKind.Break] = FourKindsInAnySeries.Weights.Classes[SegmentKind.Break],
            [SegmentKind.Opening] = ThemeWeights(SegmentFeature.OpeningWindow, SegmentFeature.PastOpeningShare, -2.8),
            [SegmentKind.Ending] = ThemeWeights(SegmentFeature.EndingWindow, SegmentFeature.PastEndingShare, -2.8),
        }),
        FourKindsInAnySeries.StationOffsets,
        FourKindsInAnySeries.SeriesOffsets,
        FourKindsInAnySeries.Thresholds,
        new Dictionary<string, FeatureScale>(FourKindsInAnySeries.Standardisation)
        {
            [SegmentFeature.LoudnessRange] = new(10, 1),
        });

    private static SegmentClassWeights ThemeWeights(string place, string past, double advert = -2.0, double multiple = -1.0)
    {
        Dictionary<string, double> features = new(StringComparer.Ordinal)
        {
            [place] = 4.8,
            [SegmentFeature.IsThemeLength] = 0.5,
            [SegmentFeature.LoudnessRange] = -0.4,
            [SegmentFeature.IsShortMultipleOf15s] = advert,
            [past] = 4.0,
        };

        if (multiple is not 0)
        {
            features[SegmentFeature.IsMultipleOf15s] = multiple;
        }

        return new(-6.0, features);
    }

}
