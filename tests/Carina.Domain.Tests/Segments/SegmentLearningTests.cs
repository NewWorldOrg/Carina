using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.LearningWorld;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLearningTests
{
    private const int Weeks = 5;

    [Fact(DisplayName = "the same recordings, catalog and labels give an identical candidate and the same figures, whatever order the recordings come in")]
    public void TheSameLabelsGiveTheSameCandidate()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        recordings.Add(Judged(RecordingId.New(), Weeks));

        SegmentLearned once = Learn(recordings, sounds);
        SegmentLearned again = Learn([.. Enumerable.Reverse(recordings)], [.. Enumerable.Reverse(sounds)]);

        Assert.Equal(once.Candidate, again.Candidate);
        Assert.Equal(Figures(once.Evaluation.Metrics), Figures(again.Evaluation.Metrics));
        Assert.Equal(Figures(once.InUse.Metrics), Figures(again.InUse.Metrics));
        Assert.Equal(once.Evaluation.RecommendedThreshold, again.Evaluation.RecommendedThreshold);
    }

    [Fact(DisplayName = "BR-CD-004: learning taken a step at a time comes to what learning at once does, and a stop between steps throws the round away")]
    public void LearningTakenAStepAtATimeComesToTheSame()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        recordings.Add(Judged(RecordingId.New(), Weeks));
        SegmentSearch search = new([SegmentThresholds.Initial], [0.1, 1], [10]);
        LearningCatalog catalog = new(sounds, SeriesLinks.None);

        SegmentLearningRun run = SegmentLearning.Begin(recordings, catalog, InUse, search, SegmentSettingsStanding.DefaultSkipThreshold, Now);
        int steps = 1;

        while (!run.Step(CancellationToken.None))
        {
            steps++;
        }

        SegmentLearned once = SegmentLearning.Learn(recordings, catalog, InUse, search, SegmentSettingsStanding.DefaultSkipThreshold, Now);
        SegmentLearningRun stopped = SegmentLearning.Begin(recordings, catalog, InUse, search, SegmentSettingsStanding.DefaultSkipThreshold, Now);
        stopped.Step(CancellationToken.None);

        Assert.Equal(4, steps);
        Assert.Equal(once.Candidate, run.Result().Candidate);
        Assert.Equal(Figures(once.Evaluation.Metrics), Figures(run.Result().Evaluation.Metrics));
        Assert.Throws<InvalidOperationException>(() => run.Step(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(stopped.Result);
        Assert.Throws<OperationCanceledException>(() => stopped.Step(new CancellationToken(canceled: true)));
        Assert.Throws<OperationCanceledException>(() => SegmentLearning.Learn(recordings, catalog, InUse, search, SegmentSettingsStanding.DefaultSkipThreshold, Now, new CancellationToken(canceled: true)));
    }

    [Fact(DisplayName = "each confirmed recording is judged by a model learned from the others alone, and a model that learned from it too would score it higher")]
    public void EachRecordingIsJudgedWithoutItsOwnLabels()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        recordings.Add(Judged(RecordingId.New(), Weeks));
        LearningCatalog catalog = new(sounds, SeriesLinks.None);

        SegmentLearned learned = Learn(recordings, sounds);
        SegmentComparison[] apart =
        [
            .. recordings.Select(left => Compared(left, catalog, Learn([.. recordings.Where(recording => recording != left)], sounds).Candidate)),
        ];
        SegmentComparison[] together = [.. recordings.Select(recording => Compared(recording, catalog, learned.Candidate))];

        Assert.Equal(Figures(SegmentMetrics.Of(apart, SegmentSettingsStanding.DefaultSkipThreshold, SkipReturns.None)), Figures(learned.Evaluation.Metrics));
        Assert.True(Recall(together) > learned.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "the catalog's stretches from recordings made after the one judged change nothing, and the same stretches made before it raise the score")]
    public void LaterStretchesOfTheCatalogAreLeftOut()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        RecordingId judged = RecordingId.New();
        recordings.Add(Judged(judged, Weeks));
        CatalogEntryId advert = CatalogEntryId.New();
        CatalogSound later = new(advert, TimeSpan.FromSeconds(75), [Heard(advert, Story, Weeks, 450, 75, judged), Heard(advert, Across, Weeks, 200, 75), Heard(advert, Further, Weeks + 1, 100, 75)]);
        CatalogSound earlier = new(advert, TimeSpan.FromSeconds(75), [Heard(advert, Story, Weeks, 450, 75, judged), Heard(advert, Across, Weeks - 1, 200, 75), Heard(advert, Further, Weeks - 2, 100, 75)]);

        SegmentLearned without = Learn(recordings, sounds);
        SegmentLearned withLater = Learn(recordings, [.. sounds, later]);
        SegmentLearned withEarlier = Learn(recordings, [.. sounds, earlier]);

        Assert.Equal(Figures(without.Evaluation.Metrics), Figures(withLater.Evaluation.Metrics));
        Assert.Equal(without.Candidate, withLater.Candidate);
        Assert.True(withEarlier.Evaluation.Metrics!.BoundaryRecallWithin2s > without.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "an answer the judged recording itself gave on a stretch of the catalog is not heard, and the same answer given on an earlier recording raises the score")]
    public void TheJudgedRecordingsOwnAnswerIsNotHeard()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        RecordingId judged = RecordingId.New();
        recordings.Add(Judged(judged, Weeks));
        CatalogEntryId sound = CatalogEntryId.New();

        SegmentLearned unanswered = Learn(recordings, [.. sounds, Sound(sound, judged, null, null)]);
        SegmentLearned answeredByItself = Learn(recordings, [.. sounds, Sound(sound, judged, SegmentKind.Break, null)]);
        SegmentLearned answeredEarlier = Learn(recordings, [.. sounds, Sound(sound, judged, null, SegmentKind.Break)]);

        Assert.Equal(Figures(unanswered.Evaluation.Metrics), Figures(answeredByItself.Evaluation.Metrics));
        Assert.True(answeredEarlier.Evaluation.Metrics!.BoundaryRecallWithin2s > unanswered.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "no label of a later recording stands on the judged recording's answer through the catalog, and the same answer given on another recording raises the score")]
    public void NoLaterLabelStandsOnTheJudgedAnswer()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks, adverts: false);
        RecordingId judged = RecordingId.New();
        RecordingId later = RecordingId.New();
        recordings.Add(Judged(judged, Weeks));
        recordings.Add(Unanswered(later, Weeks + 1));
        CatalogEntryId sting = CatalogEntryId.New();

        SegmentLearned unanswered = Learn(recordings, [.. sounds, Sting(sting, judged, later, null, null)]);
        SegmentLearned answeredByTheJudged = Learn(recordings, [.. sounds, Sting(sting, judged, later, SegmentKind.Break, null)]);
        SegmentLearned answeredInBetween = Learn(recordings, [.. sounds, Sting(sting, judged, later, null, SegmentKind.Break)]);

        Assert.Equal(Figures(unanswered.Evaluation.Metrics), Figures(answeredByTheJudged.Evaluation.Metrics));
        Assert.True(answeredInBetween.Evaluation.Metrics!.BoundaryRecallWithin2s > unanswered.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "the version in use is judged on the same recordings beside the skips viewers made with its buttons in the last seven days, and a candidate has offered none")]
    public void TheVersionInUseIsJudgedOnTheSameRecordings()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        ViewerAction[] actions =
        [
            .. Enumerable.Range(0, 8).Select(at => new ViewerAction(Now.AddDays(-1).AddMinutes(at), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(121), TimeSpan.FromSeconds(150), 1)),
            new ViewerAction(Now.AddDays(-1).AddMinutes(9), ViewerActionKind.Returned, SegmentKind.Break, TimeSpan.FromSeconds(160), TimeSpan.FromSeconds(140), 1),
            new ViewerAction(Now.AddDays(-9), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(121), TimeSpan.FromSeconds(150), 1),
        ];
        recordings.Add(Judged(RecordingId.New(), Weeks, actions));

        SegmentLearned learned = Learn(recordings, sounds);

        Assert.Equal((0.125, 8), (learned.InUse.Metrics!.ReturnedAfterSkipShare!.Value, learned.InUse.Metrics.Skips));
        Assert.Equal((null, 0), (learned.Evaluation.Metrics!.ReturnedAfterSkipShare, learned.Evaluation.Metrics.Skips));
        Assert.Equal(Weeks + 1, learned.InUse.Evidence.ConfirmedRecordings);
        Assert.Equal((Weeks * 4) + 6, learned.Evaluation.Evidence.Boundaries);
        Assert.Equal(Now.AddDays(-1).AddMinutes(9), learned.Evaluation.Evidence.LabelsUntil);
        StationMetrics station = Assert.Single(learned.Evaluation.Stations);
        Assert.Equal((Story.Episode(0).NetworkId, Story.Episode(0).ServiceId, Weeks + 1), (station.NetworkId, station.ServiceId, station.ConfirmedRecordings));
        Assert.Equal(Figures(learned.Evaluation.Metrics), Figures(station.Metrics));
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
    }

    [Fact(DisplayName = "a version chosen by hand stays in use whatever a round of learning makes")]
    public void AVersionChosenByHandStaysWhateverIsLearned()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        SegmentModelVersion chosen = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Active, true, SegmentModel.First, InUse.TrainedAt, InUse.AdoptedAt, null, InUse.AdoptedAt, null);

        SegmentLearned learned = SegmentLearning.Learn(recordings, new LearningCatalog(sounds, SeriesLinks.None), chosen, OneSetting, SegmentSettingsStanding.DefaultSkipThreshold, Now);

        Assert.Equal(AdoptionVerdict.ChosenByHand, learned.Verdict);
        Assert.NotNull(learned.Evaluation.Metrics);
    }

    [Fact(DisplayName = "the search keeps the setting that finds the most boundaries, wherever among the settings it is tried")]
    public void TheSearchKeepsTheSettingThatFindsTheMost()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks + 1, quiet: 120);
        SegmentThresholds deaf = new(-70, 0.10, 0.30, 1.0, -3.0, 1, 10, 10);
        SegmentSearch search = new([deaf, SegmentThresholds.Initial], [1, 0.1], [10]);

        SegmentLearned learned = Learn(recordings, sounds, search);

        Assert.Equal(new SegmentThresholds(-50, 0.10, 0.30, 1.0, -3.0, 1, 10, 10), learned.Candidate.Thresholds);
        Assert.Equal(1, learned.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "with no recording confirmed, only the first setting is learned, nothing is judged and the evidence is too little")]
    public void WithNothingConfirmedTheFirstSettingIsLearned()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks, confirmed: false);

        SegmentLearned learned = Learn(recordings, sounds, SegmentSearch.Default);

        Assert.Equal(SegmentThresholds.Initial, learned.Candidate.Thresholds);
        Assert.Null(learned.Evaluation.Metrics);
        Assert.Null(learned.InUse.Metrics);
        Assert.Empty(learned.Evaluation.Stations);
        Assert.Null(learned.Evaluation.RecommendedThreshold);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
    }

    [Fact(DisplayName = "the default search tries nine readings with nine pulls each, the initial setting first")]
    public void TheDefaultSearchStartsFromTheInitialSetting()
    {
        Assert.Equal(81, SegmentSearch.Default.Settings.Count);
        Assert.Equal(SegmentThresholds.Initial, SegmentSearch.Default.Settings[0]);
        Assert.Equal(9, SegmentSearch.Default.Settings.Select(setting => (setting.SilenceDbfs, setting.BlackLevel, setting.SceneChange, setting.GridToleranceSec)).Distinct().Count());
    }

    private static CatalogSound Sound(CatalogEntryId sound, RecordingId judged, SegmentKind? ownAnswer, SegmentKind? earlierAnswer)
        => new(sound, TimeSpan.FromSeconds(75), [Heard(sound, Story, Weeks, 450, 75, judged, ownAnswer), Heard(sound, StoryEarlier, Weeks, 100, 75, answer: earlierAnswer)]);

    private static CatalogSound Sting(CatalogEntryId sound, RecordingId judged, RecordingId later, SegmentKind? judgedAnswer, SegmentKind? betweenAnswer)
        => new(
            sound,
            TimeSpan.FromSeconds(29),
            [
                Heard(sound, Story, Weeks, 330.5, 29, judged, judgedAnswer),
                Heard(sound, StoryEarlier, Weeks + 1, 330.5, 29, answer: betweenAnswer),
                Heard(sound, Story, Weeks + 1, 330.5, 29, later),
            ]);

    private static SegmentComparison Compared(LearningRecording recording, LearningCatalog catalog, SegmentModel model)
        => new(
            recording.Context.NetworkId,
            recording.Context.ServiceId,
            recording.Answer!.Segments,
            SegmentDetector.Detect(recording.Evidence, recording.Context.Hearing(catalog.HeardBy(recording.Id, recording.StartedAt)), model));

    private static double? Recall(IReadOnlyList<SegmentComparison> compared)
        => SegmentMetrics.Of(compared, SegmentSettingsStanding.DefaultSkipThreshold, SkipReturns.None)!.BoundaryRecallWithin2s;
}
