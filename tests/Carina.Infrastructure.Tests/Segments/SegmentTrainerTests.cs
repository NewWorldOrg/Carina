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

    private readonly HeldSegmentAnswers answers = new();

    private readonly HeldSkipEvents skips = new();

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

        TrainingMaterial material = await Trainer().GatherAsync(harness.Versions.Active, Now, Cancel);

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

        TrainingMaterial material = await Trainer().GatherAsync(harness.Versions.Active, Now, Cancel);

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

        TrainingMaterial material = await Trainer().GatherAsync(harness.Versions.Active, Now, Cancel);

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

        TrainingMaterial material = await Trainer().GatherAsync(harness.Versions.Active, Now, Cancel);

        Assert.Equal([readable], material.Recordings.Select(recording => recording.Id));
        (LogLevel level, string line, Exception? failure) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(unreadable.Wire, line, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(failure);
    }

    [Fact(DisplayName = "BR-CD-008: with nothing to learn from no candidate is learned; with something, one is learned on the lowest priority and judged against the version in use")]
    public async Task ACandidateIsLearnedOnlyFromSomething()
    {
        Assert.Null(await Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, Cancel));

        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 130]);
        await AnswerAsync(recording, confirmed: true, (0, 100, SegmentKind.Programme), (100, 130, SegmentKind.Break), (130, Seconds, SegmentKind.Programme));
        long ran = harness.Lowest.Ran;

        SegmentLearned? learned = await Trainer().TrainAsync(harness.Versions.Active, SegmentSettingsStanding.DefaultSkipThreshold, Now, Cancel);

        Assert.NotNull(learned);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
        Assert.Equal(1, learned.Evaluation.Evidence.ConfirmedRecordings);
        Assert.Equal(SegmentThresholds.Initial.SilenceDbfs, learned.Candidate.Thresholds.SilenceDbfs);
        Assert.True(harness.Lowest.Ran > ran);
    }

    private static SegmentSpan Span(double from, double to, SegmentKind kind) => new(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to), kind, null);

    private SegmentTrainer Trainer()
        => new(
            harness.Detecting(),
            harness.Worklist,
            harness.Catalog,
            new HeldCatalogWorklist(harness.Records, harness.Data),
            answers,
            skips,
            harness.Detections,
            harness.Lowest,
            OneSetting,
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
