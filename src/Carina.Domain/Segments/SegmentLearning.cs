using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// What one round of learning made: the candidate, how it did, how the version in use did on the same
/// recordings, and whether the candidate takes its place.
/// </summary>
public sealed record SegmentLearned(SegmentModel Candidate, SegmentEvaluation Evaluation, SegmentEvaluation InUse, AdoptionVerdict Verdict);

/// <summary>
/// Learns a candidate from every recording handed over and judges it on the confirmed ones. Each recording
/// is read with the catalog as it would have heard it (<see cref="LearningCatalog"/>) and labelled by
/// <see cref="SegmentLabels"/>; only labels of programme and of breaks are learned from. Every setting of the
/// search is judged by leave-one-out in time: each confirmed recording is detected again with a model fitted
/// to every other recording's labels, none of them read with its answer through the catalog, and compared
/// with its answer. The setting chosen skips no more than
/// <see cref="SegmentAdoption.MostMoreProgrammeSkippedSecondsAnHour"/> second of programme an hour more than
/// the version in use, where one does, then finds the most boundaries within two seconds, then the fewest
/// extra ones, then skips the least programme; a tie keeps the setting tried first. With nothing confirmed,
/// the first setting is the only one tried. The candidate is fitted to every label with the chosen setting,
/// and its figures are those of its leave-one-out. The version in use is judged on the same recordings as it
/// stands, beside what viewers did with its buttons over the last <see cref="ReturnsOver"/>; a candidate has
/// offered none. Nothing is drawn at random, and the recordings are taken in the order they started, so the
/// same recordings, catalog and labels give the same candidate.
/// </summary>
public static class SegmentLearning
{
    public static readonly TimeSpan ReturnsOver = TimeSpan.FromDays(7);

    public static SegmentLearned Learn(
        IReadOnlyList<LearningRecording> recordings,
        LearningCatalog catalog,
        SegmentModelVersion active,
        SegmentSearch search,
        double skipThreshold,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        SegmentLearningRun run = Begin(recordings, catalog, active, search, skipThreshold, now);

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
        double skipThreshold,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(search);

        if (!(skipThreshold is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(skipThreshold), skipThreshold, "A threshold is a probability, from 0 to 1.");
        }

        Learner[] learners = [.. recordings.OrderBy(recording => recording.StartedAt).ThenBy(recording => recording.Id.Value).Select(recording => new Learner(recording, catalog))];
        int[] judged = [.. Enumerable.Range(0, learners.Length).Where(at => learners[at].Judged)];
        Judging judging = new(learners, skipThreshold, LabelsUntil(recordings));
        SegmentThresholds[] settings = [.. judged.Length is 0 ? search.Settings.Take(1) : search.Settings];

        return new SegmentLearningRun(new Round(learners, judged, judging, active, settings, skipThreshold, now));
    }

    /// <summary>
    /// What is learned from when <paramref name="left"/> is judged, beside every other recording's labels as they
    /// are: the labels of each other recording whose hearing stands on the answer of the one judged, read again
    /// where that answer is set aside.
    /// </summary>
    private static Dictionary<int, IReadOnlyList<SegmentSample>> ReadAgain(Labelled[] labelled, int left, SegmentThresholds reading)
    {
        Learner judged = labelled[left].Learner;

        return Enumerable.Range(0, labelled.Length)
            .Where(at => at != left && labelled[at].Learner.Hears(judged))
            .ToDictionary(at => at, at => labelled[at].Learner.Labelled(reading, judged.Recording.Id).Samples);
    }

    private static Trial Tried(Prepared prepared, int[] judged, SegmentThresholds setting, double skipThreshold, CancellationToken cancellationToken)
    {
        Labelled[] labelled = prepared.Labelled;
        IReadOnlyList<SegmentSample>[] each = [.. labelled.Select(learned => learned.Samples)];
        SegmentFit full = SegmentRegression.Fit([.. each.SelectMany(samples => samples)], setting);
        List<SegmentComparison> compared = [];

        for (int fold = 0; fold < judged.Length; fold++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int left = judged[fold];
            SegmentModel apart = SegmentRegression.Fit(LeaveOneOut.Samples(each, left, prepared.ReadAgain[fold]), setting, full.Model).Model;

            compared.Add(labelled[left].Learner.Compared(SegmentDetector.Detect(labelled[left].Pieces, apart)));
        }

        return new Trial(full.Model, compared, SegmentMetrics.Of(compared, skipThreshold, SkipReturns.None));
    }

    private static bool Better(SegmentMetrics? tried, SegmentMetrics? best, SegmentMetrics? inUse)
    {
        if (tried is null || best is null)
        {
            return tried is not null;
        }

        bool triedHolds = Holds(tried, inUse);

        if (triedHolds != Holds(best, inUse))
        {
            return triedHolds;
        }

        double triedRecall = tried.BoundaryRecallWithin2s ?? -1;
        double bestRecall = best.BoundaryRecallWithin2s ?? -1;

        if (triedRecall != bestRecall)
        {
            return triedRecall > bestRecall;
        }

        return tried.ExtraBoundariesPerRecording != best.ExtraBoundariesPerRecording
            ? tried.ExtraBoundariesPerRecording < best.ExtraBoundariesPerRecording
            : tried.ProgrammeSkippedSecondsPerHour < best.ProgrammeSkippedSecondsPerHour;
    }

    private static bool Holds(SegmentMetrics metrics, SegmentMetrics? inUse)
        => inUse is null || metrics.ProgrammeSkippedSecondsPerHour <= inUse.ProgrammeSkippedSecondsPerHour + SegmentAdoption.MostMoreProgrammeSkippedSecondsAnHour;

    private static DateTime? LabelsUntil(IReadOnlyList<LearningRecording> recordings)
    {
        DateTime[] given =
        [
            .. recordings.Select(recording => recording.Answer).OfType<GivenAnswer>().Where(answer => !answer.Withdrawn).Select(answer => answer.AnsweredAt),
            .. recordings.SelectMany(recording => recording.Actions).Select(action => action.HappenedAt),
        ];

        return given.Length is 0 ? null : given.Max();
    }

    private sealed record Trial(SegmentModel Model, IReadOnlyList<SegmentComparison> Compared, SegmentMetrics? Metrics);

    /// <summary>
    /// One reading of the search made ready to try its settings with: every recording labelled, and for each
    /// recording judged, the labels read again without its answer.
    /// </summary>
    private sealed record Prepared(Labelled[] Labelled, IReadOnlyList<Dictionary<int, IReadOnlyList<SegmentSample>>> ReadAgain);

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
        double skipThreshold,
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

            if (inUse is not SegmentEvaluation used)
            {
                inUse = judging.Evaluated(
                    [.. judged.Select(at => learners[at].Compared(SegmentDetector.Detect(learners[at].Read(active.Thresholds), active.Model)))],
                    learner => SkipReturns.Of(learner.Recording.Actions, active.Number, now - ReturnsOver, now));

                return;
            }

            IGrouping<(double, double, double, double, double), SegmentThresholds> reading = readings[readingAt];

            if (prepared is not { } ready)
            {
                prepared = Prepare(reading.First(), cancellationToken);

                return;
            }

            Trial trial = Tried(ready, judged, reading.ElementAt(settingAt), skipThreshold, cancellationToken);

            if (best is null || Better(trial.Metrics, best.Metrics, used.Metrics))
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
            List<Dictionary<int, IReadOnlyList<SegmentSample>>> readAgain = [];

            foreach (int left in judged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                readAgain.Add(ReadAgain(labelled, left, reading));
            }

            return new Prepared(labelled, readAgain);
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

    internal sealed class Learner(LearningRecording recording, LearningCatalog catalog)
    {
        private readonly SegmentContext context = recording.Context.Hearing(catalog.HeardBy(recording.Id, recording.StartedAt));

        public LearningRecording Recording => recording;

        public bool Judged => recording.Answer is { Judges: true };

        public RecordingPieces Read(SegmentThresholds thresholds) => SegmentDetector.Read(recording.Evidence, context, thresholds);

        public bool Hears(Learner other) => catalog.HearsTheAnswerOf(recording.Id, recording.StartedAt, other.Recording.Id);

        public Labelled Labelled(SegmentThresholds thresholds, RecordingId? heldOut = null)
        {
            SegmentContext heard = heldOut is null ? context : recording.Context.Hearing(catalog.HeardBy(recording.Id, recording.StartedAt, heldOut));
            RecordingPieces pieces = SegmentDetector.Read(recording.Evidence, heard, thresholds);
            IReadOnlyList<SegmentLabel?> labels = SegmentLabels.Of(pieces.Pieces, heard, recording.Answer, recording.Actions);
            string? series = pieces.Series is string named && SegmentModel.IsASeries(named) ? named : null;
            SegmentSample[] samples =
            [
                .. pieces.Pieces
                    .Zip(labels)
                    .Where(pair => pair.Second is { Kind: SegmentKind.Programme or SegmentKind.Break })
                    .Select(pair => new SegmentSample(pair.First.Features, pieces.Station, series, pair.Second!.Kind, pair.Second.Weight)),
            ];

            return new Labelled(this, pieces, samples);
        }

        public SegmentComparison Compared(SegmentDetection detection)
            => new(context.NetworkId, context.ServiceId, recording.Answer!.Segments, detection);
    }

    internal sealed class Judging(Learner[] learners, double skipThreshold, DateTime? labelsUntil)
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
                        SegmentMetrics.Of([.. group], skipThreshold, Summed(learners.Where(learner => SameStation(learner, group.First())), returns))!)),
            ];

            return new SegmentEvaluation(
                SegmentMetrics.Of(compared, skipThreshold, Summed(learners, returns)),
                stations,
                EvaluationEvidence.Of(compared, labelsUntil),
                SegmentMetrics.RecommendedThreshold(compared));
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
