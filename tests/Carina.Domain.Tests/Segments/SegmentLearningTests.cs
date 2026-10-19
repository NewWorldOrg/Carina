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
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks, adverts: true);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);

        SegmentLearned once = Learn([.. recordings, judged], [.. sounds, .. heard]);
        SegmentLearned again = Learn([judged, .. Enumerable.Reverse(recordings)], [.. Enumerable.Reverse(heard), .. Enumerable.Reverse(sounds)]);

        Assert.Equal(once.Candidate, again.Candidate);
        Assert.Equal(Figures(once.Evaluation.Metrics), Figures(again.Evaluation.Metrics));
        Assert.Equal(Figures(once.InUse.Metrics), Figures(again.InUse.Metrics));
        Assert.Equal(once.Evaluation.Evidence, again.Evaluation.Evidence);
    }

    [Fact(DisplayName = "BR-CD-004: learning taken a step at a time comes to what learning in one piece makes, and a stop between steps throws the round away")]
    public void LearningTakenAStepAtATimeComesToTheSame()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);
        recordings.Add(judged);
        SegmentSearch search = new([SegmentThresholds.Initial], [0.1, 1], [10]);
        LearningCatalog catalog = new([.. sounds, .. heard], SeriesLinks.None);

        SegmentLearningRun run = SegmentLearning.Begin(recordings, catalog, InUse, search, Now);
        int steps = 1;

        while (!run.Step(CancellationToken.None))
        {
            steps++;
        }

        SegmentLearned stepped = run.Result();
        SegmentLearned whole = SegmentLearning.Learn(recordings, catalog, InUse, search, Now);
        SegmentLearningRun stopped = SegmentLearning.Begin(recordings, catalog, InUse, search, Now);
        stopped.Step(CancellationToken.None);

        Assert.Equal(4, steps);
        Assert.Equal(whole.Candidate, stepped.Candidate);
        Assert.Equal(Figures(whole.Evaluation.Metrics), Figures(stepped.Evaluation.Metrics));
        Assert.Equal(Figures(whole.InUse.Metrics), Figures(stepped.InUse.Metrics));
        Assert.Equal((whole.Verdict, whole.Evaluation.Evidence), (stepped.Verdict, stepped.Evaluation.Evidence));
        Assert.Throws<InvalidOperationException>(() => run.Step(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(stopped.Result);
        Assert.Throws<OperationCanceledException>(() => stopped.Step(new CancellationToken(canceled: true)));
        Assert.Throws<OperationCanceledException>(() => SegmentLearning.Learn(recordings, catalog, InUse, search, Now, new CancellationToken(canceled: true)));
    }

    [Fact(DisplayName = "BR-CD-008: a recording judged is detected hearing none of the entries heard in it, by a model learned from the others alone, against what the catalog is sure of in it, and the version in use is judged the same way")]
    public void ARecordingIsJudgedWithoutItsOwnEntries()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);
        LearningCatalog catalog = new([.. sounds, .. heard], SeriesLinks.None);

        SegmentLearned learned = Learn([.. taught, judged], [.. sounds, .. heard]);
        SegmentModel apart = Learn(taught, sounds).Candidate;

        Assert.Equal(Figures(Metrics(Compared(judged, catalog, apart, hearing: false))), Figures(learned.Evaluation.Metrics));
        Assert.Equal(Figures(Metrics(Compared(judged, catalog, InUse.Model, hearing: false))), Figures(learned.InUse.Metrics));
        Assert.True(Metrics(Compared(judged, catalog, learned.Candidate, hearing: true))!.BoundaryRecallWithin2s > learned.Evaluation.Metrics!.BoundaryRecallWithin2s);
        Assert.Equal(4, Compared(judged, catalog, apart, hearing: false).Truth.Boundaries.Count);
    }

    [Fact(DisplayName = "BR-CD-008: the labels of an entry first heard after the recording judged started are not learned from in its fold, and the same entries heard long before are")]
    public void EntriesFirstHeardLaterAreSetAside()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);
        (List<LearningRecording> later, List<CatalogSound> heardLater) = Taught(3, fromWeek: Weeks + 1, blackAdverts: true);
        (List<LearningRecording> longAgo, List<CatalogSound> heardLongAgo) = Taught(3, fromWeek: Weeks + 1, blackAdverts: true, heardLongAgo: true);

        LearningCatalog catalogLater = new([.. sounds, .. heard, .. heardLater], SeriesLinks.None);
        LearningCatalog catalogLongAgo = new([.. sounds, .. heard, .. heardLongAgo], SeriesLinks.None);

        SegmentLearned without = Learn([.. taught, judged], [.. sounds, .. heard]);
        SegmentLearned withLater = Learn([.. taught, judged, .. later], [.. sounds, .. heard, .. heardLater]);
        SegmentLearned withLongAgo = Learn([.. taught, judged, .. longAgo], [.. sounds, .. heard, .. heardLongAgo]);

        Assert.Equal(
            Figures(Fold(judged, [.. taught, .. later], catalogLater, recording => later.Contains(recording) ? [] : catalogLater.HeardBy(recording.Id, recording.StartedAt))),
            Figures(withLater.Evaluation.Metrics));
        Assert.Equal(
            Figures(Fold(judged, [.. taught, .. longAgo], catalogLongAgo, recording => catalogLongAgo.HeardBy(recording.Id, recording.StartedAt))),
            Figures(withLongAgo.Evaluation.Metrics));
        Assert.Equal(Figures(without.Evaluation.Metrics), Figures(withLater.Evaluation.Metrics));
        Assert.NotEqual(Figures(withLater.Evaluation.Metrics), Figures(withLongAgo.Evaluation.Metrics));
        Assert.NotEqual(without.Candidate, withLater.Candidate);
    }

    [Fact(DisplayName = "BR-CD-008: the earliest recording judged, every other entry first heard after it, is detected by a model with nothing to learn from, and the round still comes to a result")]
    public void AFoldWithNothingToLearnFromStillJudges()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks, adverts: true);

        SegmentLearned learned = Learn(taught, sounds);

        Assert.Equal(new EvaluationEvidence(Weeks, Weeks * 2, null), learned.Evaluation.Evidence);
        Assert.NotNull(learned.Evaluation.Metrics);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
    }

    [Fact(DisplayName = "BR-CD-007: with openings labelled the candidate tells four kinds apart and finds an opening the catalog hears, and the version in use, telling two, finds none of those the catalog is sure of")]
    public void OpeningsLabelledMakeAFourKindCandidate()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks, openings: true);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks, opening: true);
        LearningCatalog catalog = new([.. sounds, .. heard], SeriesLinks.None);

        SegmentLearned learned = Learn([.. taught, judged], [.. sounds, .. heard]);

        Assert.Equal(4, learned.Candidate.Kinds);
        Assert.Single(Themes(Compared(judged, catalog, learned.Candidate, hearing: true).Shown));
        Assert.Equal(new KindMetrics(SegmentKind.Opening, 0), learned.InUse.Metrics!.Kinds.Single(kind => kind.Kind is SegmentKind.Opening));
        Assert.NotNull(learned.Evaluation.Metrics!.Kinds.Single(kind => kind.Kind is SegmentKind.Opening).Recall);
    }

    [Fact(DisplayName = "A-051: a round with no opening or ending labelled keeps the opening and the ending of the four-kind version in use as they are, learns only the break, and finds the same opening on a recording the catalog hears one in")]
    public void ARoundWithNoOpeningLabelledKeepsTheVersionsOpening()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);
        (LearningRecording opened, List<CatalogSound> opening) = Judged(RecordingId.New(), Weeks + 1, opening: true);
        LearningCatalog catalog = new([.. sounds, .. heard, .. opening], SeriesLinks.None);
        SegmentModel inUse = FourKindsInUse.Model;

        SegmentLearned learned = Learn([.. taught, judged], [.. sounds, .. heard], active: FourKindsInUse);
        SegmentModel candidate = learned.Candidate;

        Assert.Equal(4, candidate.Kinds);
        Assert.Equal(inUse.Weights.Classes[SegmentKind.Opening], candidate.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(inUse.Weights.Classes[SegmentKind.Ending], candidate.Weights.Classes[SegmentKind.Ending]);
        Assert.All(
            [SegmentFeature.CatalogOpeningShare, SegmentFeature.PastOpeningShare, SegmentFeature.CatalogEndingShare, SegmentFeature.PastEndingShare],
            name => Assert.Equal(inUse.Standardisation[name], candidate.Standardisation[name]));
        Assert.NotEqual(inUse.Weights.Classes[SegmentKind.Break], candidate.Weights.Classes[SegmentKind.Break]);
        Assert.Equal(Themes(Compared(opened, catalog, inUse, hearing: true).Shown), Themes(Compared(opened, catalog, candidate, hearing: true).Shown));
        Assert.Single(Themes(Compared(opened, catalog, candidate, hearing: true).Shown));
    }

    [Fact(DisplayName = "A-051: a round with openings labelled and no ending learns the opening afresh and keeps the ending of the four-kind version in use as it is")]
    public void ARoundWithOpeningsLabelledLearnsTheOpeningAfresh()
    {
        (List<LearningRecording> taught, List<CatalogSound> sounds) = Taught(Weeks, openings: true);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks, opening: true);

        SegmentLearned learned = Learn([.. taught, judged], [.. sounds, .. heard], active: FourKindsInUse);

        Assert.Equal(4, learned.Candidate.Kinds);
        Assert.NotEqual(FourKindsInUse.Model.Weights.Classes[SegmentKind.Opening], learned.Candidate.Weights.Classes[SegmentKind.Opening]);
        Assert.Equal(FourKindsInUse.Model.Weights.Classes[SegmentKind.Ending], learned.Candidate.Weights.Classes[SegmentKind.Ending]);
        Assert.Equal(FourKindsInUse.Model.Standardisation[SegmentFeature.CatalogEndingShare], learned.Candidate.Standardisation[SegmentFeature.CatalogEndingShare]);
    }

    [Fact(DisplayName = "the version in use is judged on the same recordings beside the skips viewers made with its buttons in the last seven days, a candidate has offered none, and the evidence counts the recordings the catalog is sure of an advert in")]
    public void TheVersionInUseIsJudgedOnTheSameRecordings()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        ViewerAction[] actions =
        [
            .. Enumerable.Range(0, 8).Select(at => new ViewerAction(Now.AddDays(-1).AddMinutes(at), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(121), TimeSpan.FromSeconds(150), 1)),
            new ViewerAction(Now.AddDays(-1).AddMinutes(9), ViewerActionKind.Returned, SegmentKind.Break, TimeSpan.FromSeconds(160), TimeSpan.FromSeconds(140), 1),
            new ViewerAction(Now.AddDays(-9), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(121), TimeSpan.FromSeconds(150), 1),
        ];
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks, actions);

        SegmentLearned learned = Learn([.. recordings, judged], [.. sounds, .. heard]);

        Assert.Equal((0.125, 8), (learned.InUse.Metrics!.ReturnedAfterSkipShare!.Value, learned.InUse.Metrics.Skips));
        Assert.Equal((null, 0), (learned.Evaluation.Metrics!.ReturnedAfterSkipShare, learned.Evaluation.Metrics.Skips));
        Assert.Equal(new EvaluationEvidence(1, 4, Now.AddDays(-1).AddMinutes(9)), learned.Evaluation.Evidence);
        Assert.Equal(learned.Evaluation.Evidence, learned.InUse.Evidence);
        StationMetrics station = Assert.Single(learned.Evaluation.Stations);
        Assert.Equal((Story.Episode(0).NetworkId, Story.Episode(0).ServiceId, 1), (station.NetworkId, station.ServiceId, station.Recordings));
        Assert.Equal(Figures(learned.Evaluation.Metrics), Figures(station.Metrics));
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
    }

    [Fact(DisplayName = "a version chosen by hand stays in use whatever a round of learning makes")]
    public void AVersionChosenByHandStaysWhateverIsLearned()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);
        SegmentModelVersion chosen = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Active, true, SegmentModel.First, InUse.TrainedAt, InUse.AdoptedAt, null, InUse.AdoptedAt, null);

        SegmentLearned learned = SegmentLearning.Learn([.. recordings, judged], new LearningCatalog([.. sounds, .. heard], SeriesLinks.None), chosen, OneSetting, Now);

        Assert.Equal(AdoptionVerdict.ChosenByHand, learned.Verdict);
        Assert.NotNull(learned.Evaluation.Metrics);
    }

    [Theory(DisplayName = "BR-CD-008: the search keeps the setting that finds the most boundaries, wherever among the settings it is tried")]
    [InlineData(1e6, 0.1)]
    [InlineData(0.1, 1e6)]
    public void TheSearchKeepsTheSettingThatFindsTheMost(double first, double second)
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);
        (LearningRecording judged, List<CatalogSound> heard) = Judged(RecordingId.New(), Weeks);

        SegmentLearned learned = Learn([.. recordings, judged], [.. sounds, .. heard], new SegmentSearch([SegmentThresholds.Initial], [first, second], [10]));
        SegmentLearned pulledFlat = Learn([.. recordings, judged], [.. sounds, .. heard], new SegmentSearch([SegmentThresholds.Initial], [1e6], [10]));
        SegmentLearned free = Learn([.. recordings, judged], [.. sounds, .. heard], new SegmentSearch([SegmentThresholds.Initial], [0.1], [10]));

        Assert.Equal(0.1, learned.Candidate.Thresholds.Lambda);
        Assert.Equal(Figures(free.Evaluation.Metrics), Figures(learned.Evaluation.Metrics));
        Assert.True(free.Evaluation.Metrics!.BoundaryRecallWithin2s > pulledFlat.Evaluation.Metrics!.BoundaryRecallWithin2s);
    }

    [Fact(DisplayName = "with no recording the catalog is sure of anything in, only the first setting is learned, nothing is judged and the evidence is too little")]
    public void WithNothingJudgedTheFirstSettingIsLearned()
    {
        (List<LearningRecording> recordings, List<CatalogSound> sounds) = Taught(Weeks);

        SegmentLearned learned = Learn(recordings, sounds, SegmentSearch.Default);

        Assert.Equal(SegmentThresholds.Initial, learned.Candidate.Thresholds);
        Assert.Null(learned.Evaluation.Metrics);
        Assert.Null(learned.InUse.Metrics);
        Assert.Empty(learned.Evaluation.Stations);
        Assert.Equal(new EvaluationEvidence(0, 0, null), learned.Evaluation.Evidence);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
    }

    [Fact(DisplayName = "the default search tries nine readings with nine pulls each, the initial setting first")]
    public void TheDefaultSearchStartsFromTheInitialSetting()
    {
        Assert.Equal(81, SegmentSearch.Default.Settings.Count);
        Assert.Equal(SegmentThresholds.Initial, SegmentSearch.Default.Settings[0]);
        Assert.Equal(9, SegmentSearch.Default.Settings.Select(setting => (setting.SilenceDbfs, setting.BlackLevel, setting.SceneChange, setting.GridToleranceSec)).Distinct().Count());
    }

    private static (TimeSpan Starts, TimeSpan Ends, SegmentKind Kind)[] Themes(IReadOnlyList<SegmentSpan> shown)
        => [.. shown.Where(span => span.Kind is SegmentKind.Opening or SegmentKind.Ending).Select(span => (span.Starts, span.Ends, span.Kind))];

    /// <summary>
    /// The recording detected with <paramref name="model"/>, hearing what it heard of the catalog when it was made or
    /// nothing of it, against what the catalog is sure of in it now.
    /// </summary>
    private static SegmentComparison Compared(LearningRecording recording, LearningCatalog catalog, SegmentModel model, bool hearing)
        => new(
            recording.Context.NetworkId,
            recording.Context.ServiceId,
            CatalogTruth.Of(catalog.HeardNow(recording.Id), recording.Evidence.Length),
            SegmentDetector.Detect(recording.Evidence, recording.Context.Hearing(hearing ? catalog.HeardBy(recording.Id, recording.StartedAt) : []), model));

    private static SegmentMetrics? Metrics(SegmentComparison compared) => SegmentMetrics.Of([compared], SkipReturns.None);

    /// <summary>
    /// The figures of <paramref name="judged"/> detected hearing nothing of the catalog by a model fitted with the one
    /// setting to the labels of <paramref name="others"/>, each read hearing what it heard of <paramref name="catalog"/>
    /// when it was made and labelled as if it heard only what <paramref name="labelledBy"/> gives.
    /// </summary>
    private static SegmentMetrics? Fold(
        LearningRecording judged,
        IReadOnlyList<LearningRecording> others,
        LearningCatalog catalog,
        Func<LearningRecording, IReadOnlyList<CatalogHeard>> labelledBy)
    {
        SegmentThresholds setting = OneSetting.Settings[0];
        SegmentModel model = SegmentRegression.Fit([.. others.SelectMany(recording => Samples(recording, catalog, labelledBy(recording), setting))], setting).Model;
        SegmentDetection detection = SegmentDetector.Detect(SegmentDetector.Read(judged.Evidence, judged.Context.Hearing([]), setting), model);

        return Metrics(new SegmentComparison(
            judged.Context.NetworkId,
            judged.Context.ServiceId,
            CatalogTruth.Of(catalog.HeardNow(judged.Id), judged.Evidence.Length),
            detection));
    }

    private static IEnumerable<SegmentSample> Samples(LearningRecording recording, LearningCatalog catalog, IReadOnlyList<CatalogHeard> labelledBy, SegmentThresholds setting)
    {
        RecordingPieces pieces = SegmentDetector.Read(recording.Evidence, recording.Context.Hearing(catalog.HeardBy(recording.Id, recording.StartedAt)), setting);
        IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces.Pieces, recording.Context.Hearing(labelledBy), recording.Actions);
        return pieces.Pieces
            .Zip(labels)
            .Where(pair => pair.Second is not null)
            .Select(pair => new SegmentSample(pair.First.Features, pieces.Station, pieces.Series, pair.Second!.Kind, pair.Second.Weight));
    }
}
