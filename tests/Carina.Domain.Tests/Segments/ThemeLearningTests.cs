using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.ThemeWorld;

namespace Carina.Domain.Tests.Segments;

public sealed class ThemeLearningTests
{
    [Fact(DisplayName = "BR-CD-006: learned from the openings and the endings the catalog hears in two series, the candidate finds those of the first episode of a third series, which the catalog hears nothing of and no earlier episode tells, where the version that knew them only from earlier episodes finds none, and leaves the insert song and the adverts beside the opening as they are")]
    public void TheOpeningAndTheEndingAreLearnedForAnySeries()
    {
        (List<LearningRecording> mystery, List<CatalogSound> heardInMystery) = Episodes(Mystery, 2);
        (List<LearningRecording> voyage, List<CatalogSound> heardInVoyage) = Episodes(Voyage, 2);
        (List<LearningRecording> garden, List<CatalogSound> heardInGarden) = Episodes(Garden, 1);

        SegmentModel candidate = Learn([.. mystery, .. voyage], [.. heardInMystery, .. heardInVoyage], SegmentModel.FourKindsBelowPauses).Candidate;
        SegmentDetection first = Unheard(garden[0], candidate);

        Assert.DoesNotContain(heardInGarden, sound => sound.Length.TotalSeconds is ThemeLasts);
        Assert.Equal(4, candidate.Kinds);
        Assert.Equal(
            [(OpeningStarts, OpeningStarts + ThemeLasts, SegmentKind.Opening), (EndingStarts, EndingStarts + ThemeLasts, SegmentKind.Ending)],
            Themes(first));
        Assert.Contains(first.Segments, span => span.Kind is SegmentKind.Break && span.Starts.TotalSeconds is OpeningStarts + ThemeLasts);
        Assert.Empty(Themes(Unheard(garden[0], SegmentModel.FourKindsBelowPauses)));
    }

    [Fact(DisplayName = "BR-CD-008: a series' opening and ending held out are judged by what the other series taught, so the figures of the opening and the ending say how the first episode of a series is found; the version made by hand for any series finds them as well, and the breaks as the version hearing silence from -55 dBFS does")]
    public void ThemesHeldOutAreFoundByWhatOtherSeriesTaught()
    {
        (List<LearningRecording> mystery, List<CatalogSound> heardInMystery) = Episodes(Mystery, 2);
        (List<LearningRecording> voyage, List<CatalogSound> heardInVoyage) = Episodes(Voyage, 2);
        (List<LearningRecording> garden, List<CatalogSound> heardInGarden) = Episodes(Garden, 2);
        List<LearningRecording> recordings = [.. mystery, .. voyage, .. garden];
        List<CatalogSound> sounds = [.. heardInMystery, .. heardInVoyage, .. heardInGarden];

        SegmentLearned forAnySeries = Learn(recordings, sounds, SegmentModel.FourKindsInAnySeries);
        SegmentLearned belowPauses = Learn(recordings, sounds, SegmentModel.FourKindsBelowPauses);

        Assert.All([forAnySeries.Evaluation.Metrics!, forAnySeries.InUse.Metrics!], metrics =>
        {
            Assert.True(Recall(metrics, SegmentKind.Opening) > 0.95);
            Assert.True(Recall(metrics, SegmentKind.Ending) > 0.95);
        });
        Assert.Equal((0, 0), (Recall(belowPauses.InUse.Metrics!, SegmentKind.Opening), Recall(belowPauses.InUse.Metrics!, SegmentKind.Ending)));
        Assert.True(Recall(belowPauses.Evaluation.Metrics!, SegmentKind.Opening) > 0);
        Assert.Equal(belowPauses.InUse.Metrics!.BreakRecall, forAnySeries.InUse.Metrics!.BreakRecall);
        Assert.True(forAnySeries.InUse.Metrics.BoundaryRecallWithin1s > belowPauses.InUse.Metrics.BoundaryRecallWithin1s);
    }

    private static double? Recall(SegmentMetrics metrics, SegmentKind kind) => metrics.Kinds.Single(measured => measured.Kind == kind).Recall;
}
