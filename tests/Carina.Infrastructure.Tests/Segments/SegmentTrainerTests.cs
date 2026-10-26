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

    private readonly ListedSkips skips = new();

    private readonly HeldSeekEvents seeks = new();

    private readonly KeptLines<SegmentTrainer> log = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "BR-CD-006: learning reads every recording with data read from it and a stretch of the catalog, a skip event or a seek, kept or not, and no other, each with its skip events and its seeks")]
    public async Task LearningReadsEveryRecordingWithSomethingToLearnItBy()
    {
        RecordingId skipped = await harness.ReadAsync(5, "架空の物語", cuts: [100]);
        RecordingId sought = await harness.ReadAsync(4, "架空の物語");
        RecordingId heard = await harness.ReadAsync(3, "架空のクイズ");
        await harness.ReadAsync(1, "架空の物語");
        harness.Worklist.Kept.Remove(skipped);
        await SkippedAsync(skipped);
        await seeks.TakeAsync(SeekEvent.Of(sought, new ViewerSeek(Now, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(140))), Cancel);
        HeardIn(CatalogEntryId.New(), (heard, 100), (heard, 200));

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Equal([skipped, sought, heard], material.Recordings.Select(recording => recording.Id));
        LearningRecording first = material.Recordings[0];
        Assert.Equal(TimeSpan.FromSeconds(Seconds), first.Evidence.Length);
        Assert.Equal(harness.Records.Row(skipped)!.Programme.RecordingStartedAt, first.StartedAt);
        Assert.Equal([ViewerActionKind.Skipped], first.Actions.Select(action => action.Action));
        Assert.Empty(first.Seeks);
        Assert.Equal([(SeekMeans.Step, TimeSpan.FromSeconds(140))], material.Recordings[1].Seeks.Select(seek => (seek.Means, seek.To)));
        Assert.Empty(material.Recordings[2].Actions);
        Assert.Empty(material.Recordings[2].Seeks);
    }

    [Fact(DisplayName = "BR-CD-005: the catalog is handed over with every stretch, as each recording would have heard it and as it stands now")]
    public async Task TheCatalogIsHandedOverWithEveryStretch()
    {
        RecordingId earlier = await harness.ReadAsync(3, "架空の物語");
        RecordingId later = await harness.ReadAsync(1, "架空のクイズ");
        CatalogEntryId advert = CatalogEntryId.New();
        HeardIn(advert, (earlier, 100), (later, 50));
        DateTime earlierStarted = harness.Records.Row(earlier)!.Programme.RecordingStartedAt;
        DateTime laterStarted = harness.Records.Row(later)!.Programme.RecordingStartedAt;

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Empty(material.Catalog.HeardBy(earlier, earlierStarted));
        Assert.Equal(
            [(50d, (SegmentKind?)SegmentKind.Break, (double?)0.9, (CatalogEntryId?)advert)],
            material.Catalog.HeardBy(later, laterStarted).Select(stretch => (stretch.Starts.TotalSeconds, stretch.Kind, stretch.Confidence, stretch.Entry)));
        Assert.Equal([(100d, (double?)0.9)], material.Catalog.HeardNow(earlier).Select(stretch => (stretch.Starts.TotalSeconds, stretch.Confidence)));
        Assert.True(material.Catalog.SetAsideFor(earlier, earlierStarted).Contains(advert));
    }

    [Fact(DisplayName = "a recording whose data cannot be read is left out of the round and said so, and the rest are learned from")]
    public async Task ARecordingThatCannotBeReadIsLeftOut()
    {
        RecordingId unreadable = await harness.ReadAsync(3, "架空の物語", station: 2);
        RecordingId readable = await harness.ReadAsync(2, "架空の物語");
        await SkippedAsync(unreadable);
        await SkippedAsync(readable);
        harness.Worklist.Unreadable.Add(unreadable);

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        Assert.Equal([readable], material.Recordings.Select(recording => recording.Id));
        (LogLevel level, string line, Exception? failure) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains(unreadable.Wire, line, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(failure);
    }

    [Fact(DisplayName = "BR-CD-008: with nothing to learn from no candidate is learned; with something, one is learned on the lowest priority and judged against the version in use on the recordings the catalog is sure of an advert in")]
    public async Task ACandidateIsLearnedOnlyFromSomething()
    {
        Assert.Null((await Trainer().TrainAsync(harness.Versions.Active, Now, GoOn, Cancel)).Learned);

        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 115]);
        RecordingId elsewhere = await harness.ReadAsync(1, "架空のクイズ");
        HeardIn(CatalogEntryId.New(), (recording, 100), (elsewhere, 50));
        long ran = harness.Lowest.Ran;

        SegmentLearned? learned = (await Trainer().TrainAsync(harness.Versions.Active, Now, GoOn, Cancel)).Learned;

        Assert.NotNull(learned);
        Assert.Equal(AdoptionVerdict.TooLittleEvidence, learned.Verdict);
        Assert.Equal(2, learned.Evaluation.Evidence.Recordings);
        Assert.Equal(learned.Evaluation.Evidence, learned.InUse.Evidence);
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
            await SkippedAsync(recording);
        }

        Counted goOn = new(stopAt: 2);

        Trained trained = await Trainer().TrainAsync(harness.Versions.Active, Now, goOn.AskedAsync, Cancel);

        Assert.Equal(Trained.StoppedPartway, trained);
        Assert.Equal(2, goOn.Asked);
        Assert.Contains(second, harness.Worklist.Read);
        Assert.DoesNotContain(third, harness.Worklist.Read);
    }

    [Fact(DisplayName = "BR-CD-004: learning told to stop after a setting is tried stops there and learns nothing, each step its own piece of work at the lowest priority")]
    public async Task LearningStopsAtASettingBoundary()
    {
        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 115]);
        RecordingId elsewhere = await harness.ReadAsync(1, "架空のクイズ");
        HeardIn(CatalogEntryId.New(), (recording, 100), (elsewhere, 50));
        SegmentTrainingSettings threeSettings = new() { Search = new SegmentSearch([SegmentThresholds.Initial], [0.1, 1, 10], [10]) };
        Counted goOn = new(stopAt: 4);
        Counted throughout = new(stopAt: int.MaxValue);
        long before = harness.Lowest.Ran;

        Trained stopped = await Trainer(threeSettings).TrainAsync(harness.Versions.Active, Now, goOn.AskedAsync, Cancel);
        long ranUntilTheStop = harness.Lowest.Ran - before;
        Trained learned = await Trainer(threeSettings).TrainAsync(harness.Versions.Active, Now, throughout.AskedAsync, Cancel);

        Assert.Equal(Trained.StoppedPartway, stopped);
        Assert.Equal(4, goOn.Asked);
        Assert.NotNull(learned.Learned);
        Assert.Equal(2 + 1 + 1 + 3, throughout.Asked);
        Assert.True(ranUntilTheStop >= 4);
    }

    [Fact(DisplayName = "a stop asked for by the process stopping interrupts learning between steps")]
    public async Task TheProcessStoppingInterruptsLearning()
    {
        RecordingId recording = await harness.ReadAsync(2, "架空の物語", cuts: [100, 130]);
        await SkippedAsync(recording);
        using CancellationTokenSource stopping = new();
        Counted goOn = new(stopAt: int.MaxValue, whenAsked: 2, then: stopping.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Trainer().TrainAsync(harness.Versions.Active, Now, goOn.AskedAsync, stopping.Token));
        Assert.Equal(2, goOn.Asked);
    }

    [Fact(DisplayName = "BR-CD-004: only the skip events of the recordings a round reads are read")]
    public async Task OnlyTheRoundsSkipsAreRead()
    {
        RecordingId read = await harness.ReadAsync(2, "架空の物語");
        RecordingId unread = RecordingId.New();
        await SkippedAsync(unread);
        await SkippedAsync(read);

        TrainingMaterial material = (await Trainer().GatherAsync(harness.Versions.Active, Now, GoOn, Cancel))!;

        LearningRecording learned = Assert.Single(material.Recordings);
        Assert.Equal(read, learned.Id);
        Assert.Single(learned.Actions);
        Assert.Equal([read], skips.Listed.SelectMany(asked => asked).Distinct());
    }

    private static Task<bool> GoOn(CancellationToken cancellationToken) => Task.FromResult(true);

    private SegmentTrainer Trainer(SegmentTrainingSettings? settings = null)
        => new(
            harness.Detecting(),
            new HeldTrainingWorklist(harness.Records, harness.Catalog, skips, seeks),
            harness.Worklist,
            harness.Catalog,
            new HeldCatalogWorklist(harness.Records, harness.Data),
            skips,
            seeks,
            harness.Lowest,
            settings ?? OneSetting,
            log);

    private async Task SkippedAsync(RecordingId recording)
        => await skips.TakeAsync(SkipEvent.Of(recording, new ViewerAction(Now, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115), 1)), Cancel);

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

        harness.Catalog.Entries.Add(CatalogEntry.Start(id, Fifteen, [.. heard.Select(occurrence => PlacedOccurrence.Of(occurrence, null))], SeriesLinks.None, Now));
        harness.Catalog.Occurrences.AddRange(heard);
    }
}

/// <summary>
/// The recordings a round could read, from the records, catalog, skip events and seeks held, as the store finds them.
/// </summary>
internal sealed class HeldTrainingWorklist(HeldLearningExtractions records, HeldCatalog catalog, ISkipEventRepository skips, ISeekEventRepository seeks) : ITrainingWorklist
{
    public async Task<IReadOnlyList<LearningCandidate>> CandidatesAsync(CancellationToken cancellationToken)
    {
        List<LearningCandidate> candidates = [];

        foreach (LearningExtraction record in records.All().Where(record => record.State is LearningExtractionState.Done or LearningExtractionState.Partial && record.ReadThrough > TimeSpan.Zero))
        {
            CatalogOccurrence[] heard = [.. catalog.Occurrences.Where(occurrence => occurrence.RecordingId.Equals(record.RecordingId))];
            bool adverts = heard.Any(occurrence => catalog.Entries.Any(entry => entry.Id.Equals(occurrence.EntryId) && entry.Kind is CatalogKind.Cm));

            if (heard.Length > 0
                || (await skips.ListAsync([record.RecordingId], cancellationToken)).Count > 0
                || (await seeks.ListAsync([record.RecordingId], cancellationToken)).Count > 0)
            {
                candidates.Add(new LearningCandidate(
                    record.RecordingId,
                    record.Programme.RecordingStartedAt,
                    record.ReadThrough,
                    adverts ? LearningGround.CatalogAdverts : LearningGround.Other));
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
