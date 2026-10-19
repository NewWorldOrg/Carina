using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.LearningWorld;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLearningTests
{
    private const int Weeks = 5;

    /// <summary>
    /// <see cref="JudgedAnswer"/> with an opening from 360 to 450, where the recordings learned from have programme
    /// that nothing but the earlier episodes tells from it.
    /// </summary>
    private static readonly SegmentSpan[] ThemedAnswer =
    [
        Span(0, 120, SegmentKind.Programme),
        Span(120, 150, SegmentKind.Break),
        Span(150, 330, SegmentKind.Programme),
        Span(330, 360, SegmentKind.Break),
        Span(360, 450, SegmentKind.Opening),
        Span(450, 525, SegmentKind.Break),
        Span(525, Length, SegmentKind.Programme),
    ];

    private const double PinnedWithin = 1e-6;

    private const double PinnedBias = -3.4620117697283472;

    private const string PinnedFigures = "0.846153846 0.846153846 0.000000000 0.000000 Break:0.840909:1.000000,Opening::,Ending::  0";

    /// <summary>
    /// The weights learning made of the recordings of <see cref="LearningTakenAStepAtATimeComesToTheSame"/> while it
    /// was still taken in one piece, every weight not named being nothing.
    /// </summary>
    private static readonly Dictionary<string, double> PinnedWeights = new(StringComparer.Ordinal)
    {
        ["catalogCmMultipleOf15s"] = 0.7049540571812944,
        ["catalogCmShare"] = 0.7049540571812944,
        ["catalogOccurrences"] = 0.4340280198238972,
        ["catalogSeries"] = 0.597943926388741,
        ["catalogShare"] = 0.4340280198238972,
        ["catalogStations"] = 0.597943926388741,
        ["channelCorrelation"] = -0.6927543110924417,
        ["edgeSilence"] = 1.256021509984539,
        ["fromEnd"] = 0.11143462659038852,
        ["fromStart"] = 0.09607390788697237,
        ["gridOffset"] = -1.2497351927194036,
        ["headChannelChange"] = 0.4358179902680711,
        ["headSilenceDepth"] = 0.09312847366425785,
        ["headSilenceLength"] = 0.0931284736642579,
        ["isMultipleOf15s"] = 1.000353133364359,
        ["length"] = -0.7639539660381195,
        ["sceneChangesPer10s"] = 1.486423942619604,
        ["tailChannelChange"] = 0.3763080145991857,
        ["tailSilenceDepth"] = 0.7279614349783017,
        ["tailSilenceLength"] = 0.7279614349783018,
    };

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

    [Fact(DisplayName = "BR-CD-004: learning taken a step at a time comes to the candidate and figures learning made in one piece before it was split into steps, and a stop between steps throws the round away")]
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

        SegmentLearned learned = run.Result();
        SegmentClassWeights breaks = learned.Candidate.Weights.Classes[SegmentKind.Break];
        SegmentLearningRun stopped = SegmentLearning.Begin(recordings, catalog, InUse, search, SegmentSettingsStanding.DefaultSkipThreshold, Now);
        stopped.Step(CancellationToken.None);

        Assert.Equal(4, steps);
        Assert.Equal(PinnedBias, breaks.Bias, PinnedWithin);
        Assert.All(breaks.Features, feature => Assert.Equal(PinnedWeights.GetValueOrDefault(feature.Key), feature.Value, PinnedWithin));
        Assert.Equal(PinnedWeights.Keys.Order(StringComparer.Ordinal), breaks.Features.Keys.Where(PinnedWeights.ContainsKey).Order(StringComparer.Ordinal));
        Assert.All(learned.Candidate.StationOffsets.Values.Concat(learned.Candidate.SeriesOffsets.Values), offset => Assert.Equal(0, offset, PinnedWithin));
        Assert.Equal((0.1, 10.0), (learned.Candidate.Thresholds.Lambda, learned.Candidate.Thresholds.LambdaStation));
        Assert.Equal(PinnedFigures, Figures(learned.Evaluation.Metrics));
        Assert.Equal(PinnedFigures, Figures(learned.InUse.Metrics));
        Assert.Equal((0.5, AdoptionVerdict.TooLittleEvidence, 6, 26), (learned.Evaluation.RecommendedThreshold, learned.Verdict, learned.Evaluation.Evidence.ConfirmedRecordings, learned.Evaluation.Evidence.Boundaries));
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

    [Fact(DisplayName = "BR-CD-007: with openings labelled the candidate tells four kinds apart and finds the judged recording's opening, which the version in use, telling two, cannot")]
    public void OpeningsLabelledMakeAFourKindCandidate()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Opened(Weeks);
        (LearningRecording judged, CatalogSound heard) = JudgedOpened(RecordingId.New(), Weeks);

        SegmentLearned learned = Learn([.. recordings, judged], [.. sounds, heard]);

        Assert.Equal(4, learned.Candidate.Kinds);
        Assert.True(learned.Evaluation.Metrics!.Kinds.Single(kind => kind.Kind is SegmentKind.Opening).Recall > 0.9);
        Assert.Equal(new KindMetrics(SegmentKind.Opening, 0, null), learned.InUse.Metrics!.Kinds.Single(kind => kind.Kind is SegmentKind.Opening));
        Assert.True(learned.Evaluation.Metrics.BoundaryRecallWithin2s > learned.InUse.Metrics.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "A-051: a round with no opening or ending labelled keeps the opening and the ending of the four-kind version in use as they are, learns only the break, and finds the same opening on a recording the catalog hears one in")]
    public void ARoundWithNoOpeningLabelledKeepsTheVersionsOpening()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        recordings.Add(Judged(RecordingId.New(), Weeks));
        (LearningRecording opened, CatalogSound heard) = JudgedOpened(RecordingId.New(), Weeks + 1);
        LearningCatalog catalog = new([.. sounds, heard], SeriesLinks.None);
        SegmentModel inUse = FourKindsInUse.Model;

        SegmentLearned learned = Learn(recordings, sounds, active: FourKindsInUse);
        SegmentModel candidate = learned.Candidate;

        Assert.Equal(4, candidate.Kinds);
        Assert.Equal(inUse.Weights.Classes[SegmentKind.Opening], candidate.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(inUse.Weights.Classes[SegmentKind.Ending], candidate.Weights.Classes[SegmentKind.Ending]);
        Assert.All(
            [SegmentFeature.CatalogOpeningShare, SegmentFeature.PastOpeningShare, SegmentFeature.CatalogEndingShare, SegmentFeature.PastEndingShare],
            name => Assert.Equal(inUse.Standardisation[name], candidate.Standardisation[name]));
        Assert.NotEqual(inUse.Weights.Classes[SegmentKind.Break], candidate.Weights.Classes[SegmentKind.Break]);
        Assert.Equal(Themes(Compared(opened, catalog, inUse).Shown), Themes(Compared(opened, catalog, candidate).Shown));
        Assert.Single(Themes(Compared(opened, catalog, candidate).Shown));
    }

    [Fact(DisplayName = "A-051: a round with openings labelled and no ending learns the opening afresh and keeps the ending of the four-kind version in use as it is")]
    public void ARoundWithOpeningsLabelledLearnsTheOpeningAfresh()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Opened(Weeks);
        (LearningRecording judged, CatalogSound heard) = JudgedOpened(RecordingId.New(), Weeks);

        SegmentLearned learned = Learn([.. recordings, judged], [.. sounds, heard], active: FourKindsInUse);

        Assert.Equal(4, learned.Candidate.Kinds);
        Assert.NotEqual(FourKindsInUse.Model.Weights.Classes[SegmentKind.Opening], learned.Candidate.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(FourKindsInUse.Model.Weights.Classes[SegmentKind.Ending], learned.Candidate.Weights.Classes[SegmentKind.Ending]);
        Assert.Equal(FourKindsInUse.Model.Standardisation[SegmentFeature.CatalogEndingShare], learned.Candidate.Standardisation[SegmentFeature.CatalogEndingShare]);
    }

    [Fact(DisplayName = "A-051: answers that could say only programme or a break leave the openings the catalog hears to learn from, and the round still finds the judged recording's opening")]
    public void AnswersThatCannotNameOpeningsLeaveTheCatalogsToLearnFrom()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Opened(Weeks, answersNameOpenings: false);
        (LearningRecording judged, CatalogSound heard) = JudgedOpened(RecordingId.New(), Weeks, answerNamesOpenings: false);
        LearningCatalog catalog = new([.. sounds, heard], SeriesLinks.None);

        SegmentLearned learned = Learn([.. recordings, judged], [.. sounds, heard]);

        Assert.All(recordings, recording => Assert.DoesNotContain(recording.Answer!.Segments, segment => segment.Kind is SegmentKind.Opening));
        Assert.Equal(4, learned.Candidate.Kinds);
        Assert.Single(Themes(Compared(judged, catalog, learned.Candidate).Shown));
        Assert.Equal(0, learned.Evaluation.Metrics!.ExtraBoundariesPerRecording);
    }

    [Fact(DisplayName = "A-051: each confirmed recording is judged by a model learned from the others alone, its own opening included, and a model that learned from it too would score it higher")]
    public void EachRecordingIsJudgedWithoutItsOwnOpening()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Opened(Weeks);
        (LearningRecording judged, CatalogSound heard) = JudgedOpened(RecordingId.New(), Weeks);
        recordings.Add(judged);
        sounds.Add(heard);
        LearningCatalog catalog = new(sounds, SeriesLinks.None);

        SegmentLearned learned = Learn(recordings, sounds);
        SegmentComparison[] apart =
        [
            .. recordings.Select(left => Compared(left, catalog, Learn([.. recordings.Where(recording => recording != left)], sounds).Candidate)),
        ];
        SegmentComparison[] together = [.. recordings.Select(recording => Compared(recording, catalog, learned.Candidate))];

        Assert.Equal(Figures(SegmentMetrics.Of(apart, SegmentSettingsStanding.DefaultSkipThreshold, SkipReturns.None)), Figures(learned.Evaluation.Metrics));
        Assert.True(Recall(together) >= learned.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "A-051: an opening the judged recording itself answered on a stretch of the catalog is not heard, and the same answer given on an earlier recording raises the score")]
    public void TheJudgedRecordingsOwnOpeningIsNotHeard()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Opened(Weeks);
        (LearningRecording judged, _) = JudgedOpened(RecordingId.New(), Weeks);
        recordings.Add(judged);

        SegmentLearned unanswered = Learn(recordings, [.. sounds, Theme(judged.Id, null, null)]);
        SegmentLearned answeredByItself = Learn(recordings, [.. sounds, Theme(judged.Id, SegmentKind.Opening, null)]);
        SegmentLearned answeredEarlier = Learn(recordings, [.. sounds, Theme(judged.Id, null, SegmentKind.Opening)]);

        Assert.Equal(Figures(unanswered.Evaluation.Metrics), Figures(answeredByItself.Evaluation.Metrics));
        Assert.True(answeredEarlier.Evaluation.Metrics!.BoundaryRecallWithin2s > unanswered.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "A-051: no label of a later recording stands on the judged recording's answer through the earlier episodes it remembers, and the same answer remembered of another recording raises the score")]
    public void NoLaterLabelStandsOnTheJudgedAnswerThroughThePast()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks + 2);
        RecordingId judged = taught[Weeks].Id;
        RecordingId later = taught[Weeks + 1].Id;
        List<LearningRecording> recordings = [.. taught.Take(Weeks), Judged(judged, Weeks, past: [Themed(RecordingId.New())], answer: ThemedAnswer)];

        SegmentLearned forgetting = Learn([.. recordings, Remembering(later, Weeks + 1, ThemedAnswer, [])], sounds);
        SegmentLearned rememberingTheJudged = Learn([.. recordings, Remembering(later, Weeks + 1, ThemedAnswer, [Themed(judged)])], sounds);
        SegmentLearned rememberingAnother = Learn([.. recordings, Remembering(later, Weeks + 1, ThemedAnswer, [Themed(RecordingId.New())])], sounds);

        Assert.Equal(4, rememberingAnother.Candidate.Kinds);
        Assert.Equal(Figures(forgetting.Evaluation.Metrics), Figures(rememberingTheJudged.Evaluation.Metrics));
        Assert.True(rememberingAnother.Evaluation.Metrics!.BoundaryRecallWithin2s > forgetting.Evaluation.Metrics!.BoundaryRecallWithin2s);
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

    /// <summary>
    /// A sound of 85 seconds heard in the judged recording where its opening is, and in an earlier episode in the
    /// second half of the programme, so that where it is heard says nothing of its kind.
    /// </summary>
    private static CatalogSound Theme(RecordingId judged, SegmentKind? judgedAnswer, SegmentKind? earlierAnswer)
    {
        CatalogEntryId sound = CatalogEntryId.New();

        return new(
            sound,
            TimeSpan.FromSeconds(85),
            [Heard(sound, Story, Weeks, 365, 85, judged, judgedAnswer), Heard(sound, StoryEarlier, Weeks, CatalogSeries.Lead.TotalSeconds + CatalogSeries.Midpoint + 100, 85, answer: earlierAnswer)]);
    }

    /// <summary>
    /// An earlier episode of <paramref name="recording"/> remembered by <see cref="ThemedAnswer"/> alone.
    /// </summary>
    private static PastEpisode Themed(RecordingId recording) => new(recording, TimeSpan.Zero, null, null, ThemedAnswer);

    private static (TimeSpan Starts, TimeSpan Ends, SegmentKind Kind)[] Themes(IReadOnlyList<SegmentSpan> shown)
        => [.. shown.Where(span => span.Kind is SegmentKind.Opening or SegmentKind.Ending).Select(span => (span.Starts, span.Ends, span.Kind))];

    private static SegmentComparison Compared(LearningRecording recording, LearningCatalog catalog, SegmentModel model)
        => new(
            recording.Context.NetworkId,
            recording.Context.ServiceId,
            recording.Answer!.Segments,
            SegmentDetector.Detect(recording.Evidence, recording.Context.Hearing(catalog.HeardBy(recording.Id, recording.StartedAt)), model),
            recording.Answer.NamesOpeningsAndEndings);

    private static double? Recall(IReadOnlyList<SegmentComparison> compared)
        => SegmentMetrics.Of(compared, SegmentSettingsStanding.DefaultSkipThreshold, SkipReturns.None)!.BoundaryRecallWithin2s;
}
