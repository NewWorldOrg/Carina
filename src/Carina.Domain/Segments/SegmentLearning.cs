namespace Carina.Domain.Segments;

/// <summary>
/// What one round of learning made: the candidate, how it did, how the version in use did on the same
/// recordings, and whether the candidate takes its place.
/// </summary>
public sealed record SegmentLearned(SegmentModel Candidate, SegmentEvaluation Evaluation, SegmentEvaluation InUse, AdoptionVerdict Verdict);

/// <summary>
/// Learns a candidate from every recording handed over and judges it on those the catalog is sure of something in
/// (<see cref="CatalogTruth"/>). Each recording is read with the catalog as it would have heard it
/// (<see cref="LearningCatalog"/>) and labelled by <see cref="SegmentLabels"/>; labels of the programme, of breaks, of
/// openings and of endings are learned from, and with an opening or an ending among them the candidate tells four
/// kinds apart. Beside a version in use that tells four kinds apart, the candidate keeps that version's opening, or
/// its ending, as it is when nothing is labelled to learn it from, so that a round with nothing to learn one of them
/// from never takes it away; the same holds in every fold of the leave-one-out. Every setting of the search is judged
/// by leave-one-out in time: each recording judged is detected again with the entries set aside for it
/// (<see cref="LearningCatalog.SetAsideFor"/>) heard nowhere in it, which leaves it hearing nothing of the catalog,
/// and its earlier episodes remembered without the part of what they were taken to hold that lies over a stretch of
/// one of them (<see cref="LearningCatalog.Forgetting"/>), with a model fitted to every other recording's labels, none of them
/// standing on an entry set aside for it, and compared with what the catalog is sure of in it. The setting chosen finds the most boundaries within two seconds,
/// then the most seconds of adverts (<see cref="SegmentAdoption.Better"/>); a tie keeps the setting tried first. With
/// nothing judged, the first setting is the only one tried. The candidate is fitted to every label with the chosen
/// setting, and its figures are those of its leave-one-out. The version in use is judged on the same recordings, each
/// detected as it stands without the entries set aside for it, beside what viewers did with its buttons over the last
/// <see cref="ReturnsOver"/>; a candidate has offered none. Nothing is drawn at random, and the recordings are taken
/// in the order they started, so the same recordings, catalog and labels give the same candidate.
/// </summary>
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

    /// <summary>
    /// The same learning as <see cref="Learn"/>, to be taken a step at a time.
    /// </summary>
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

        Learner[] learners = [.. recordings.OrderBy(recording => recording.StartedAt).ThenBy(recording => recording.Id.Value).Select(recording => new Learner(recording, catalog))];
        int[] judged = [.. Enumerable.Range(0, learners.Length).Where(at => learners[at].Judged)];
        Judging judging = new(learners, LabelsUntil(recordings));
        SegmentThresholds[] settings = [.. judged.Length is 0 ? search.Settings.Take(1) : search.Settings];

        return new SegmentLearningRun(new Round(learners, judged, judging, active, settings, now));
    }

    /// <summary>
    /// What is learned from when <paramref name="left"/> is judged, beside every other recording's labels as they
    /// are: the labels of each other recording that hears an entry set aside for the one judged, read again without
    /// those entries.
    /// </summary>
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
        SegmentFit full = SegmentRegression.Fit([.. each.SelectMany(samples => samples)], setting, kept: kept);
        List<SegmentComparison> compared = [];

        for (int fold = 0; fold < judged.Length; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int left = judged[fold];
            SegmentModel apart = SegmentRegression.Fit(LeaveOneOut.Samples(each, left, Relabelled(labelled, left)), setting, full.Model, kept).Model;

            compared.Add(labelled[left].Learner.Compared(SegmentDetector.Detect(prepared.Judged[fold], apart)));
        }

        return new Trial(full.Model, compared, SegmentMetrics.Of(compared, SkipReturns.None));
    }

    private static bool Better(SegmentMetrics? tried, SegmentMetrics? best)
    {
        if (tried is null || best is null)
        {
            return tried is not null;
        }

        return SegmentAdoption.Better(tried, best);
    }

    private static DateTime? LabelsUntil(IReadOnlyList<LearningRecording> recordings)
    {
        DateTime[] given = [.. recordings.SelectMany(recording => recording.Actions).Select(action => action.HappenedAt)];

        return given.Length is 0 ? null : given.Max();
    }

    private sealed record Trial(SegmentModel Model, IReadOnlyList<SegmentComparison> Compared, SegmentMetrics? Metrics);

    /// <summary>
    /// One reading of the search made ready to try its settings with: every recording labelled, and each recording
    /// judged read again without the entries set aside for it.
    /// </summary>
    private sealed record Prepared(Labelled[] Labelled, IReadOnlyList<RecordingPieces> Judged);

    /// <summary>
    /// The steps of one round: the version in use judged, then each reading of the search prepared and each of its
    /// settings tried, the best kept.
    /// </summary>
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

            if (best is null || Better(trial.Metrics, best.Metrics))
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

    /// <summary>
    /// One recording as a round reads it: what it heard of the catalog when it was made, which its readings and the
    /// catalog's labels stand on; everything the catalog hears in it now, which says what the catalog is sure of in it
    /// and keeps the programme by elimination off every stretch the catalog has, a first airing included; and the
    /// entries set aside when it is judged, which it is read again without, its earlier episodes forgetting what they
    /// were taken to hold over a stretch of one of them.
    /// </summary>
    internal sealed class Learner
    {
        private readonly LearningCatalog catalog;

        private readonly IReadOnlyList<CatalogHeard> heard;

        private readonly IReadOnlyList<CatalogHeard> everything;

        public Learner(LearningRecording recording, LearningCatalog catalog)
        {
            this.catalog = catalog;
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

        /// <summary>
        /// Whether this recording hears a stretch of one of <paramref name="entries"/>.
        /// </summary>
        public bool Hears(IReadOnlySet<CatalogEntryId> entries)
            => entries.Count is not 0 && heard.Any(stretch => stretch.Entry is CatalogEntryId entry && entries.Contains(entry));

        public Labelled Labelled(SegmentThresholds thresholds)
        {
            RecordingPieces pieces = Read(thresholds);

            return new Labelled(this, pieces, Samples(pieces));
        }

        /// <summary>
        /// The samples <paramref name="pieces"/> of this recording are learned from, labelled as if the catalog did
        /// not hear <paramref name="without"/>; every stretch the catalog has in the recording, those of
        /// <paramref name="without"/> included, still keeps the segments it lies over from being taken for the programme.
        /// </summary>
        public IReadOnlyList<SegmentSample> Samples(RecordingPieces pieces, IReadOnlySet<CatalogEntryId>? without = null)
        {
            IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces.Pieces, Context(without), Recording.Actions, everything);
            string? series = pieces.Series is string named && SegmentModel.IsASeries(named) ? named : null;

            return
            [
                .. pieces.Pieces
                    .Zip(labels)
                    .Where(pair => pair.Second is not null)
                    .Select(pair => new SegmentSample(pair.First.Features, pieces.Station, series, pair.Second!.Kind, pair.Second.Weight)),
            ];
        }

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

/// <summary>
/// One round of <see cref="SegmentLearning"/> taken a step at a time, so that whoever runs it can give way, or stop
/// and throw what was done away, between steps: the version in use judged first, then each reading of the search
/// made ready, then each of its settings tried. Once every step has been taken, <see cref="Result"/> is what
/// <see cref="SegmentLearning.Learn"/> gives.
/// </summary>
public sealed class SegmentLearningRun
{
    private readonly SegmentLearning.Round round;

    internal SegmentLearningRun(SegmentLearning.Round round)
    {
        this.round = round;
    }

    public bool Done => round.Done;

    /// <summary>
    /// Takes the next step, and says whether every step has now been taken.
    /// </summary>
    public bool Step(CancellationToken cancellationToken)
    {
        round.Step(cancellationToken);

        return round.Done;
    }

    public SegmentLearned Result() => round.Result();
}
