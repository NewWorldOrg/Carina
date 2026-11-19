namespace Carina.Domain.Segments;

public sealed record SegmentLearned(SegmentModel Candidate, SegmentEvaluation Evaluation, SegmentEvaluation InUse, AdoptionVerdict Verdict)
{
    public bool IsKept => Evaluation.Evidence.Recordings is not 0;
}

public static class SegmentLearning
{
    public static readonly TimeSpan ReturnsOver = TimeSpan.FromDays(7);

    public static SegmentLearned Learn(
        IReadOnlyList<LearningRecording> recordings,
        LearningCatalog catalog,
        SegmentModelVersion active,
        SegmentSearch search,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        SegmentLearningRun run = Begin(recordings, catalog, active, search, now);

        while (!run.Step(cancellationToken))
        {
        }

        return run.Result();
    }

    public static SegmentLearningRun Begin(
        IReadOnlyList<LearningRecording> recordings,
        LearningCatalog catalog,
        SegmentModelVersion active,
        SegmentSearch search,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(search);

        Learner[] learners = [.. recordings.OrderBy(recording => recording.StartedAt).ThenBy(recording => recording.Id.Value).Select(recording => new Learner(recording, catalog, active))];
        int[] judged = [.. Enumerable.Range(0, learners.Length).Where(at => learners[at].Judged)];
        Judging judging = new(learners, LabelsUntil(recordings));
        SegmentThresholds[] settings = [.. judged.Length is 0 ? search.Settings.Take(1) : search.Settings];

        return new SegmentLearningRun(new Round(learners, judged, judging, active, settings, now));
    }

    private static Dictionary<int, IReadOnlyList<SegmentSample>> Relabelled(Labelled[] labelled, int left)
    {
        IReadOnlySet<CatalogEntryId> setAside = labelled[left].Learner.SetAside;

        return Enumerable.Range(0, labelled.Length)
            .Where(at => at != left && labelled[at].Learner.Hears(setAside))
            .ToDictionary(at => at, at => labelled[at].Learner.Samples(labelled[at].Pieces, setAside));
    }

    private static Trial Tried(Prepared prepared, int[] judged, SegmentThresholds setting, SegmentModel inUse, CancellationToken cancellationToken)
    {
        Labelled[] labelled = prepared.Labelled;
        IReadOnlyList<SegmentSample>[] each = [.. labelled.Select(learned => learned.Samples)];
        SegmentModel? kept = inUse.Kinds is 4 ? inUse : null;
        SegmentFit full = SegmentRegression.Fit([.. each.SelectMany(samples => samples)], setting, kept: kept, toward: inUse);
        List<SegmentComparison> compared = [];

        for (int fold = 0; fold < judged.Length; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int left = judged[fold];
            SegmentModel apart = SegmentRegression.Fit(LeaveOneOut.Samples(each, left, Relabelled(labelled, left)), setting, full.Model, kept, inUse).Model;

            compared.Add(labelled[left].Learner.Compared(SegmentDetector.Detect(prepared.Judged[fold], apart)));
        }

        return new Trial(full.Model, compared, SegmentMetrics.Of(compared, SkipReturns.None));
    }

    private static DateTime? LabelsUntil(IReadOnlyList<LearningRecording> recordings)
    {
        DateTime[] given =
        [
            .. recordings.SelectMany(recording => recording.Actions).Select(action => action.HappenedAt),
            .. recordings.SelectMany(recording => recording.Seeks).Select(seek => seek.HappenedAt),
        ];

        return given.Length is 0 ? null : given.Max();
    }

    private sealed record Trial(SegmentModel Model, IReadOnlyList<SegmentComparison> Compared, SegmentMetrics? Metrics);

    private sealed record Prepared(Labelled[] Labelled, IReadOnlyList<RecordingPieces> Judged);

    internal sealed class Round(
        Learner[] learners,
        int[] judged,
        Judging judging,
        SegmentModelVersion active,
        SegmentThresholds[] settings,
        DateTime now)
    {
        private readonly IGrouping<(double, double, double, double, double), SegmentThresholds>[] readings =
        [
            .. settings.GroupBy(setting => (setting.SilenceDbfs, setting.BlackLevel, setting.SceneChange, setting.GridToleranceSec, setting.GridPenalty)),
        ];

        private SegmentEvaluation? inUse;

        private Prepared? prepared;

        private int readingAt;

        private int settingAt;

        private Trial? best;

        public bool Done => inUse is not null && readingAt == readings.Length;

        public void Step(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Done)
            {
                throw new InvalidOperationException("Every step of this round has been taken.");
            }

            if (inUse is null)
            {
                inUse = judging.Evaluated(
                    [.. judged.Select(at => learners[at].Compared(SegmentDetector.Detect(learners[at].Read(active.Thresholds, learners[at].SetAside), active.Model)))],
                    learner => SkipReturns.Of(learner.Recording.Actions, active.Number, now - ReturnsOver, now));

                return;
            }

            IGrouping<(double, double, double, double, double), SegmentThresholds> reading = readings[readingAt];

            if (prepared is not { } ready)
            {
                prepared = Prepare(reading.First(), cancellationToken);

                return;
            }

            Trial trial = Tried(ready, judged, reading.ElementAt(settingAt), active.Model, cancellationToken);

            if (best is null || SegmentAdoption.Prefers(trial.Metrics, best.Metrics, inUse))
            {
                best = trial;
            }

            Advance(reading.Count());
        }

        public SegmentLearned Result()
        {
            if (!Done || inUse is not SegmentEvaluation used || best is not { } chosen)
            {
                throw new InvalidOperationException("A round has a result once every step has been taken.");
            }

            SegmentEvaluation evaluation = judging.Evaluated(chosen.Compared, _ => SkipReturns.None);

            return new SegmentLearned(chosen.Model, evaluation, used, SegmentAdoption.Judge(active, used, evaluation));
        }

        private Prepared Prepare(SegmentThresholds reading, CancellationToken cancellationToken)
        {
            Labelled[] labelled = [.. learners.Select(learner => learner.Labelled(reading))];
            List<RecordingPieces> apart = [];

            foreach (int left in judged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                apart.Add(learners[left].Read(reading, learners[left].SetAside));
            }

            return new Prepared(labelled, apart);
        }

        private void Advance(int settingsOfTheReading)
        {
            settingAt++;

            if (settingAt < settingsOfTheReading)
            {
                return;
            }

            readingAt++;
            settingAt = 0;
            prepared = null;
        }
    }

    internal sealed record Labelled(Learner Learner, RecordingPieces Pieces, IReadOnlyList<SegmentSample> Samples);

    internal sealed class Learner
    {
        private readonly LearningCatalog catalog;

        private readonly SegmentModelVersion active;

        private readonly IReadOnlyList<CatalogHeard> heard;

        private readonly IReadOnlyList<CatalogHeard> everything;

        private readonly Dictionary<IReadOnlySet<CatalogEntryId>, (SegmentDetection Detection, ViewerSeeks Seeks)> inUseWithout = new(ReferenceEqualityComparer.Instance);

        private ViewerSeeks? seeks;

        private (RecordingPieces Read, SegmentDetection Detection)? inUse;

        public Learner(LearningRecording recording, LearningCatalog catalog, SegmentModelVersion active)
        {
            this.catalog = catalog;
            this.active = active;
            Recording = recording;
            heard = catalog.HeardBy(recording.Id, recording.StartedAt);
            everything = catalog.HeardNow(recording.Id);
            Truth = CatalogTruth.Of(everything, recording.Evidence);
            SetAside = Truth.Judges ? catalog.SetAsideFor(recording.Id, recording.StartedAt) : new HashSet<CatalogEntryId>();
        }

        public LearningRecording Recording { get; }

        public CatalogTruth Truth { get; }

        public IReadOnlySet<CatalogEntryId> SetAside { get; }

        public bool Judged => Truth.Judges;

        public RecordingPieces Read(SegmentThresholds thresholds, IReadOnlySet<CatalogEntryId>? without = null)
            => SegmentDetector.Read(Recording.Evidence, without is null ? Context(null) : catalog.Forgetting(Context(without), without), thresholds);

        public bool Hears(IReadOnlySet<CatalogEntryId> entries)
            => entries.Count is not 0 && heard.Any(stretch => stretch.Entry is CatalogEntryId entry && entries.Contains(entry));

        public Labelled Labelled(SegmentThresholds thresholds)
        {
            RecordingPieces read = SegmentDetector.Read(Recording.Evidence, Recording.Context.Hearing([.. heard.Select(Unsure)]), thresholds);
            IReadOnlyList<SegmentLabel?> labels = SegmentLabels.OfJoined(read.Joined, Context(null), Recording.Actions, Seeks);
            RecordingPieces kept = read with { Joined = [.. read.Joined.Where((_, at) => labels[at] is not null)] };

            return new Labelled(this, kept, Samples(kept));
        }

        public IReadOnlyList<SegmentSample> Samples(RecordingPieces pieces, IReadOnlySet<CatalogEntryId>? without = null)
        {
            if (without is null || without.Count is 0)
            {
                return SegmentSamples.Of(pieces, Context(without), Recording.Actions, everything, Seeks, InUse.Detection);
            }

            if (!inUseWithout.TryGetValue(without, out (SegmentDetection Detection, ViewerSeeks Seeks) apart))
            {
                (RecordingPieces Read, SegmentDetection Detection) read = ReadInUse(without);

                apart = (read.Detection, SeeksAgainst(read));
                inUseWithout[without] = apart;
            }

            return SegmentSamples.Of(pieces, Context(without), Recording.Actions, everything, apart.Seeks, apart.Detection);
        }

        private ViewerSeeks Seeks => seeks ??= ReadSeeks();

        private (RecordingPieces Read, SegmentDetection Detection) InUse => inUse ??= ReadInUse();

        private (RecordingPieces Read, SegmentDetection Detection) ReadInUse(IReadOnlySet<CatalogEntryId>? without = null)
        {
            RecordingPieces read = Read(active.Thresholds, without);

            return (read, SegmentDetector.Detect(read, active.Model));
        }

        private ViewerSeeks ReadSeeks() => SeeksAgainst(InUse);

        private ViewerSeeks SeeksAgainst((RecordingPieces Read, SegmentDetection Detection) reading)
            => Recording.Seeks.Count is 0
                ? ViewerSeeks.None
                : ViewerSeeks.Read(Recording.Seeks, reading.Read.Candidates, reading.Detection, TimeSpan.FromSeconds(active.Thresholds.GridToleranceSec));

        private static CatalogHeard Unsure(CatalogHeard stretch)
            => new(stretch.Starts, stretch.Ends, stretch.Kind, stretch.Occurrences, stretch.Series, stretch.Stations, entry: stretch.Entry);

        public SegmentComparison Compared(SegmentDetection detection)
            => new(Recording.Context.NetworkId, Recording.Context.ServiceId, Truth, detection);

        private SegmentContext Context(IReadOnlySet<CatalogEntryId>? without)
            => Recording.Context.Hearing(without is null ? heard : [.. heard.Where(stretch => stretch.Entry is not CatalogEntryId entry || !without.Contains(entry))]);
    }

    internal sealed class Judging(Learner[] learners, DateTime? labelsUntil)
    {
        public SegmentEvaluation Evaluated(IReadOnlyList<SegmentComparison> compared, Func<Learner, SkipReturns> returns)
        {
            StationMetrics[] stations =
            [
                .. compared
                    .GroupBy(comparison => (Network: comparison.NetworkId.Value, Service: comparison.ServiceId.Value))
                    .OrderBy(group => group.Key.Network)
                    .ThenBy(group => group.Key.Service)
                    .Select(group => new StationMetrics(
                        group.First().NetworkId,
                        group.First().ServiceId,
                        group.Count(),
                        SegmentMetrics.Of([.. group], Summed(learners.Where(learner => SameStation(learner, group.First())), returns))!)),
            ];

            return new SegmentEvaluation(
                SegmentMetrics.Of(compared, Summed(learners, returns)),
                stations,
                EvaluationEvidence.Of(compared, labelsUntil));
        }

        private static bool SameStation(Learner learner, SegmentComparison compared)
            => learner.Recording.Context.NetworkId.Equals(compared.NetworkId) && learner.Recording.Context.ServiceId.Equals(compared.ServiceId);

        private static SkipReturns Summed(IEnumerable<Learner> learners, Func<Learner, SkipReturns> returns)
        {
            SkipReturns[] each = [.. learners.Select(returns)];

            return new SkipReturns(each.Sum(counted => counted.Skips), each.Sum(counted => counted.Returned));
        }
    }
}

public sealed class SegmentLearningRun
{
    private readonly SegmentLearning.Round round;

    internal SegmentLearningRun(SegmentLearning.Round round)
    {
        this.round = round;
    }

    public bool Done => round.Done;

    public bool Step(CancellationToken cancellationToken)
    {
        round.Step(cancellationToken);

        return round.Done;
    }

    public SegmentLearned Result() => round.Result();
}
