using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.Logging;

using static Carina.Infrastructure.Tests.Segments.SegmentDetectionHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class SegmentTrainerTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    private static readonly SegmentTrainingSettings OneSetting = new() { Search = new SegmentSearch([SegmentThresholds.Initial], [1], [10]) };

    private readonly SegmentDetectionHarness harness = new();

    private readonly ListedAnswers answers = new();

    private readonly ListedSkips skips = new();

    private readonly KeptLines<SegmentTrainer> log = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "BR-CD-006: learning reads every recording with data read from it and an answer, a stretch of the catalog or a skip event, kept or not, and no other")]
    public async Task LearningReadsEveryRecordingWithSomethingToLearnItBy()
    {
        RecordingId answered = await harness.ReadAsync(5, "架空の物語", cuts: [100]);
        RecordingId skipped = await harness.ReadAsync(4, "架空の物語");
        RecordingId heard = await harness.ReadAsync(3, "架空のクイズ");
        RecordingId withdrawn = await harness.ReadAsync(2, "架空の物語");
        await harness.ReadAsync(1, "架空の物語");
        harness.Worklist.Kept.Remove(answered);
        await AnswerAsync(answered, confirmed: true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        await AnswerAsync(withdrawn, confirmed: true, (0, Seconds, SegmentKind.Programme));
        await answers.WithdrawAsync(withdrawn, Now, Cancel);
        await AnswerAsync(RecordingId.New(), confirmed: true, (0, Seconds, SegmentKind.Programme));
        await skips.TakeAsync(SkipEvent.Of(skipped, new ViewerAction(Now, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), 1)), Cancel);
        HeardIn(CatalogEntryId.New(), (heard, 100), (heard, 200));

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Equal([answered, skipped, heard], material.Recordings.Select(recording => recording.Id));
        LearningRecording first = material.Recordings[0];
        Assert.Equal((true, true, TimeSpan.FromSeconds(Seconds)), (first.Answer!.Confirmed, first.Answer.Judges, first.Evidence.Length));
        Assert.Equal(harness.Records.Row(answered)!.Programme.RecordingStartedAt, first.StartedAt);
        Assert.Equal([ViewerActionKind.Skipped], material.Recordings[1].Actions.Select(action => action.Action));
        Assert.Null(material.Recordings[2].Answer);
    }

    [Fact(DisplayName = "BR-CD-006: an answer not confirmed is read against the detection the version in use made of its recording")]
    public async Task AnAnswerNotConfirmedIsReadAgainstTheDetectionInUse()
    {
        RecordingId recording = await harness.ReadAsync(2, "架空の物語");
        await AnswerAsync(recording, confirmed: false, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        SegmentSpan[] detected = [Span(0, 115, SegmentKind.Programme), Span(115, 130, SegmentKind.Break), Span(130, Seconds, SegmentKind.Programme)];
        await harness.Detections.KeepAsync(RecordingDetection.Of(recording, SegmentModelVersion.FirstNumber, SegmentDetection.Judged(detected, TimeSpan.FromSeconds(Seconds), []), 2, Now, Now), Cancel);
        await harness.Detections.KeepAsync(RecordingDetection.Of(recording, 2, SegmentDetection.Judged([Span(0, Seconds, SegmentKind.Programme)], TimeSpan.FromSeconds(Seconds), []), 2, Now, Now), Cancel);

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Equal(detected, Assert.Single(material.Recordings).Answer!.Detected);
    }

    [Fact(DisplayName = "BR-CD-005: the catalog is handed over with every stretch, each carrying the kind the answer on its recording gives it")]
    public async Task TheCatalogCarriesTheKindsTheAnswersGive()
    {
        RecordingId answered = await harness.ReadAsync(3, "架空の物語");
        RecordingId later = await harness.ReadAsync(1, "架空のクイズ");
        await AnswerAsync(answered, confirmed: true, (0, 95, SegmentKind.Programme), (95, 120, SegmentKind.Break), (120, Seconds, SegmentKind.Programme));
        HeardIn(CatalogEntryId.New(), (answered, 100), (later, 50));
        DateTime laterStarted = harness.Records.Row(later)!.Programme.RecordingStartedAt;

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.True(material.Catalog.HearsTheAnswerOf(later, laterStarted, answered));
        Assert.Equal(
            [(50d, (SegmentKind?)SegmentKind.Break, (double?)1.0)],
            material.Catalog.HeardBy(later, laterStarted).Select(stretch => (stretch.Starts.TotalSeconds, stretch.Kind, stretch.Confidence)));
    }

    [Fact(DisplayName = "a recording whose data cannot be read is left out of the round and said so, and the rest are learned from")]
    public async Task ARecordingThatCannotBeReadIsLeftOut()
    {
        RecordingId unreadable = await harness.ReadAsync(3, "架空の物語", station: 2);
        RecordingId readable = await harness.ReadAsync(2, "架空の物語");
        await AnswerAsync(unreadable, confirmed: true, (0, Seconds, SegmentKind.Programme));
        await AnswerAsync(readable, confirmed: true, (0, Seconds, SegmentKind.Programme));
        harness.Worklist.Unreadable.Add(unreadable);

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Equal([readable], material.Recordings.Select(recording => recording.Id));
        (LogLevel level, string line, Exception? failure) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(unreadable.Wire, line, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(failure);
    }

    [Fact(DisplayName = "BR-CD-008: with nothing to learn from no candidate is learned; with something, one is learned on the lowest priority and judged against the version in use")]
    public async Task ACandidateIsLearnedOnlyFromSomething()
    {
        Assert.Null((await Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, GoOn, Cancel)).Learned);

        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 130]);
        await AnswerAsync(recording, confirmed: true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        long ran = harness.Lowest.Ran;

        SegmentLearned? learned = (await Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, GoOn, Cancel)).Learned;

        Assert.NotNull(learned);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
        Assert.Equal(1, learned.Evaluation.Evidence.ConfirmedRecordings);
        Assert.Equal(SegmentThresholds.Initial.SilenceDbfs, learned.Candidate.Thresholds.SilenceDbfs);
        Assert.True(harness.Lowest.Ran > ran);
    }

    [Fact(DisplayName = "BR-CD-004: learning told to stop after a recording is read stops there, reads no further recording and learns nothing")]
    public async Task LearningStopsAtARecordingBoundary()
    {
        RecordingId first = await harness.ReadAsync(3, "架空の物語");
        RecordingId second = await harness.ReadAsync(2, "架空の物語");
        RecordingId third = await harness.ReadAsync(1, "架空の物語");

        foreach (RecordingId recording in new[] { first, second, third })
        {
            await AnswerAsync(recording, confirmed: true, (0, Seconds, SegmentKind.Programme));
        }

        Counted goOn = new(stopAt: 2);

        Trained trained = await Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, goOn.AskedAsync, Cancel);

        Assert.Equal(Trained.StoppedPartway, trained);
        Assert.Equal(2, goOn.Asked);
        Assert.Contains(second, harness.Worklist.Read);
        Assert.DoesNotContain(third, harness.Worklist.Read);
    }

    [Fact(DisplayName = "BR-CD-004: learning told to stop after a setting is tried stops there and learns nothing, each step its own piece of work at the lowest priority")]
    public async Task LearningStopsAtASettingBoundary()
    {
        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 130]);
        await AnswerAsync(recording, confirmed: true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        SegmentTrainingSettings threeSettings = new() { Search = new SegmentSearch([SegmentThresholds.Initial], [0.1, 1, 10], [10]) };
        Counted goOn = new(stopAt: 4);
        Counted throughout = new(stopAt: int.MaxValue);
        long before = harness.Lowest.Ran;

        Trained stopped = await Trainer(threeSettings).TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, goOn.AskedAsync, Cancel);
        long ranUntilTheStop = harness.Lowest.Ran - before;
        Trained learned = await Trainer(threeSettings).TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, throughout.AskedAsync, Cancel);

        Assert.Equal(Trained.StoppedPartway, stopped);
        Assert.Equal(4, goOn.Asked);
        Assert.NotNull(learned.Learned);
        Assert.Equal(1 + 1 + 1 + 3, throughout.Asked);
        Assert.True(ranUntilTheStop >= 4);
    }

    [Fact(DisplayName = "a stop asked for by the process stopping interrupts learning between steps")]
    public async Task TheProcessStoppingInterruptsLearning()
    {
        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 130]);
        await AnswerAsync(recording, confirmed: true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        using CancellationTokenSource stopping = new();
        Counted goOn = new(stopAt: int.MaxValue, whenAsked: 2, then: stopping.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, goOn.AskedAsync, stopping.Token));
        Assert.Equal(2, goOn.Asked);
    }

    [Fact(DisplayName = "BR-CD-004: only the answers and skip events of the recordings a round reads are read")]
    public async Task OnlyTheRoundsAnswersAndSkipsAreRead()
    {
        RecordingId read = await harness.ReadAsync(2, "架空の物語");
        RecordingId unread = RecordingId.New();
        await AnswerAsync(read, confirmed: true, (0, Seconds, SegmentKind.Programme));
        await AnswerAsync(unread, confirmed: true, (0, Seconds, SegmentKind.Programme));
        await skips.TakeAsync(SkipEvent.Of(unread, new ViewerAction(Now, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), 1)), Cancel);
        await skips.TakeAsync(SkipEvent.Of(read, new ViewerAction(Now, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), 1)), Cancel);

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        LearningRecording learned = Assert.Single(material.Recordings);
        Assert.Equal(read, learned.Id);
        Assert.Single(learned.Actions);
        Assert.Equal([read], answers.Listed.SelectMany(asked => asked).Distinct());
        Assert.Equal([read], skips.Listed.SelectMany(asked => asked).Distinct());
    }

    private static Task<bool> GoOn(CancellationToken cancellationToken) => Task.FromResult(true);

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, null);

    private SegmentTrainer Trainer(SegmentTrainingSettings? settings = null)
        => new(
            harness.Detecting(),
            new HeldTrainingWorklist(harness.Records, answers, harness.Catalog, skips),
            harness.Worklist,
            harness.Catalog,
            new HeldCatalogWorklist(harness.Records, harness.Data),
            answers,
            skips,
            harness.Detections,
            harness.Lowest,
            settings ?? OneSetting,
            log);

    private async Task AnswerAsync(RecordingId recording, bool confirmed, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
        => await answers.AnswerAsync(
            RecordingAnswer.Given(
                recording,
                AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], TimeSpan.FromSeconds(Seconds)).Stretches!,
                confirmed,
                Now),
            Cancel);

    /// <summary>
    /// A fifteen-second entry of the catalog heard at each second named in each recording named.
    /// </summary>
    private void HeardIn(CatalogEntryId id, params (RecordingId Recording, double At)[] stretches)
    {
        CatalogOccurrence[] heard =
        [
            .. stretches.Select(stretch => CatalogOccurrence.Found(
                id,
                stretch.Recording,
                new SoundSpan(TimeSpan.FromSeconds(stretch.At), TimeSpan.FromSeconds(stretch.At + 15)),
                harness.Records.Row(stretch.Recording)!.Programme,
                0.05,
                Now)),
        ];

        harness.Catalog.Entries.Add(CatalogEntry.Start(id, Fifteen, [.. heard.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, Now));
        harness.Catalog.Occurrences.AddRange(heard);
    }
}

/// <summary>
/// The recordings a round could read, from the records, answers, catalog and skip events held, as the store finds them.
/// </summary>
internal sealed class HeldTrainingWorklist(HeldLearningExtractions records, ISegmentAnswerRepository answers, HeldCatalog catalog, ISkipEventRepository skips) : ITrainingWorklist
{
    public async Task<IReadOnlyList<LearningCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        List<LearningCandidate> candidates = [];

        foreach (LearningExtraction record in records.All().Where(record => record.State is LearningExtractionState.Done or LearningExtractionState.Partial && record.ReadThrough > TimeSpan.Zero))
        {
            RecordingAnswer? answer = await answers.FindAsync(record.RecordingId, cancellationToken);
            bool heard = catalog.Occurrences.Any(occurrence => occurrence.RecordingId.Equals(record.RecordingId))
                         || (await skips.ListAsync([record.RecordingId], cancellationToken)).Count > 0;
            LearningGround? ground = answer switch
            {
                { Stands: true, Confirmed: true } => LearningGround.Confirmed,
                { Stands: true } => LearningGround.Answered,
                _ => heard ? LearningGround.Other : null,
            };

            if (ground is LearningGround found)
            {
                candidates.Add(new LearningCandidate(record.RecordingId, record.Programme.RecordingStartedAt, record.ReadThrough, found));
            }
        }

        return candidates;
    }
}

/// <summary>
/// Asks whether learning may go on, says no the <c>stopAt</c>-th time, and does what a test says at the time named.
/// </summary>
internal sealed class Counted(int stopAt, int whenAsked = 0, Action? then = null)
{
    public int Asked { get; private set; }

    public Task<bool> AskedAsync(CancellationToken cancellationToken)
    {
        Asked++;

        if (Asked == whenAsked)
        {
            then?.Invoke();
        }

        return Task.FromResult(Asked < stopAt);
    }
}

/// <summary>
/// The answers held, keeping which recordings each listing named.
/// </summary>
internal sealed class ListedAnswers : ISegmentAnswerRepository
{
    private readonly HeldSegmentAnswers held = new();

    public List<IReadOnlyCollection<RecordingId>> Listed { get; } = [];

    public Task<RecordingAnswer?> FindAsync(RecordingId recording, CancellationToken cancellationToken) => held.FindAsync(recording, cancellationToken);

    public Task<IReadOnlyList<RecordingAnswer>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
    {
        Listed.Add(recordings);

        return held.ListAsync(recordings, cancellationToken);
    }

    public Task AnswerAsync(RecordingAnswer answer, CancellationToken cancellationToken) => held.AnswerAsync(answer, cancellationToken);

    public Task<bool> WithdrawAsync(RecordingId recording, DateTime at, CancellationToken cancellationToken) => held.WithdrawAsync(recording, at, cancellationToken);

    public Task<EvaluationEvidence> EvidenceAsync(CancellationToken cancellationToken) => held.EvidenceAsync(cancellationToken);
}

/// <summary>
/// The skip events held, keeping which recordings each listing named.
/// </summary>
internal sealed class ListedSkips : ISkipEventRepository
{
    private readonly HeldSkipEvents held = new();

    public List<IReadOnlyCollection<RecordingId>> Listed { get; } = [];

    public Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken) => held.TakeAsync(skip, cancellationToken);

    public Task<IReadOnlyList<SkipEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
    {
        Listed.Add(recordings);

        return held.ListAsync(recordings, cancellationToken);
    }

    public Task<SkipReturns> ReturnsAsync(int modelVersion, DateTime from, DateTime until, CancellationToken cancellationToken)
        => held.ReturnsAsync(modelVersion, from, until, cancellationToken);
}
