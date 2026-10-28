namespace Carina.Domain.Segments;

/// <summary>
/// What one round of learning made: the candidate, how it did, how the version in use did on the same
/// recordings, and whether the candidate takes its place.
/// </summary>
public sealed record SegmentLearned(SegmentModel Candidate, SegmentEvaluation Evaluation, SegmentEvaluation InUse, AdoptionVerdict Verdict);

/// <summary>
/// Learns a candidate from every recording handed over and judges it on those the catalog is sure of something in
/// (<see cref="CatalogTruth"/>). Each recording is read with the catalog as it would have heard it
/// (<see cref="LearningCatalog"/>), sure of none of it, so that no segment is fixed and neighbouring segments are read
/// as one (<see cref="JoinedPiece"/>) across its stretches as across what it does not know, and labelled by
/// <see cref="SegmentLabels"/>, its segments and those runs alike, each learned with the features detection scores it by
/// (<see cref="SegmentSamples"/>), its seeks read once against its boundary candidates and the version in use's detection
/// of it (<see cref="ViewerSeeks"/>), the same detection keeping the programme by elimination off what it surely takes for
/// an opening or an ending; labels of the programme, of breaks, of
/// openings and of endings are learned from, and with an opening or an ending among them the candidate tells four
/// kinds apart. Its weights and offsets are pulled towards the version in use's (<see cref="SegmentRegression"/>), so
/// that a kind with few labels stays near what that version finds. Beside a version in use that tells four kinds apart, the candidate keeps that version's opening, or
/// its ending, as it is when nothing is labelled to learn it from, so that a round with nothing to learn one of them
/// from never takes it away; the same holds in every fold of the leave-one-out. Every setting of the search is judged
/// by leave-one-out in time: each recording judged is detected again with the entries set aside for it
/// (<see cref="LearningCatalog.SetAsideFor"/>) heard nowhere in it, which leaves it hearing nothing of the catalog,
/// and its earlier episodes remembered without the part of what they were taken to hold that lies over a stretch of
/// one of them (<see cref="LearningCatalog.Forgetting"/>), with a model fitted to every other recording's labels, none of them
/// standing on an entry set aside for it, and compared with what the catalog is sure of in it. The setting chosen keeps the
/// openings and the endings the version in use finds, as a candidate must to be put in use, then finds the most boundaries
/// within two seconds, then the most seconds of adverts (<see cref="SegmentAdoption.Prefers"/>); when no setting keeps them,
/// the one best at boundaries is chosen all the same, and the round says it loses them. A tie keeps the setting tried first. With
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

        Learner[] learners = [.. recordings.OrderBy(recording => recording.StartedAt).ThenBy(recording => recording.Id.Value).Select(recording => new Learner(recording, catalog, active))];
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

        private readonly SegmentModelVersion active;

        private readonly IReadOnlyList<CatalogHeard> heard;

        private readonly IReadOnlyList<CatalogHeard> everything;

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

        /// <summary>
        /// Whether this recording hears a stretch of one of <paramref name="entries"/>.
        /// </summary>
        public bool Hears(IReadOnlySet<CatalogEntryId> entries)
            => entries.Count is not 0 && heard.Any(stretch => stretch.Entry is CatalogEntryId entry && entries.Contains(entry));

        /// <summary>
        /// This recording read to learn from and its samples: read hearing what it heard of the catalog when it was made,
        /// its stretches cutting segments where they start and end, but sure of none of them, so that none fixes a
        /// segment and neighbouring segments are read as one across them. A run of segments read as one that takes no
        /// label with all the catalog heard takes none without some of it either, so only the runs that take one are
        /// kept.
        /// </summary>
        public Labelled Labelled(SegmentThresholds thresholds)
        {
            RecordingPieces read = SegmentDetector.Read(Recording.Evidence, Recording.Context.Hearing([.. heard.Select(Unsure)]), thresholds);
            IReadOnlyList<SegmentLabel?> labels = SegmentLabels.OfJoined(read.Joined, Context(null), Recording.Actions, Seeks);
            RecordingPieces kept = read with { Joined = [.. read.Joined.Where((_, at) => labels[at] is not null)] };

            return new Labelled(this, kept, Samples(kept));
        }

        /// <summary>
        /// The samples <paramref name="pieces"/> of this recording are learned from, labelled as if the catalog did
        /// not hear <paramref name="without"/>; every stretch the catalog has in the recording, those of
        /// <paramref name="without"/> included, still keeps the segments it lies over from being taken for the programme.
        /// </summary>
        public IReadOnlyList<SegmentSample> Samples(RecordingPieces pieces, IReadOnlySet<CatalogEntryId>? without = null)
            => SegmentSamples.Of(pieces, Context(without), Recording.Actions, everything, Seeks, InUse.Detection);

        /// <summary>
        /// The seeks of this recording read against the boundary candidates and the detection of the version in use, as
        /// it reads the recording hearing what it heard of the catalog when it was made; read once, when first asked for.
        /// </summary>
        private ViewerSeeks Seeks => seeks ??= ReadSeeks();

        /// <summary>
        /// The recording read as the version in use reads it, hearing what it heard of the catalog when it was made, and
        /// that version's detection of it; read once, when first asked for.
        /// </summary>
        private (RecordingPieces Read, SegmentDetection Detection) InUse => inUse ??= ReadInUse();

        private (RecordingPieces Read, SegmentDetection Detection) ReadInUse()
        {
            RecordingPieces read = Read(active.Thresholds);

            return (read, SegmentDetector.Detect(read, active.Model));
        }

        private ViewerSeeks ReadSeeks()
            => Recording.Seeks.Count is 0
                ? ViewerSeeks.None
                : ViewerSeeks.Read(Recording.Seeks, InUse.Read.Candidates, InUse.Detection, TimeSpan.FromSeconds(active.Thresholds.GridToleranceSec));

        /// <summary>
        /// <paramref name="stretch"/> with its confidence not known, which fixes nothing (<see cref="SegmentFeatures"/>).
        /// </summary>
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
