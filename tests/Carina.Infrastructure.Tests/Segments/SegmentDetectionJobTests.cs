using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.SegmentDetectionHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class SegmentDetectionJobTests
{
    private static readonly double[] ABreak = [100, 130, 160];

    [Fact(DisplayName = "BR-CD-013: while learning is off no recording is detected on either path, and nothing is read or kept")]
    public async Task NothingIsDetectedWhileLearningIsOff()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId recording = await harness.ReadAsync(0, "架空の物語", cuts: ABreak);
        SegmentDetectionJob job = harness.Job();

        SegmentDetectionLook atOnce = await job.DetectMatchedAsync(recording, CancellationToken.None);
        SegmentDetectionLook inSpareTime = await job.LookAsync(CancellationToken.None);

        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.LearningOff, null), atOnce);
        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.LearningOff, null), inSpareTime);
        Assert.Equal(0, harness.Worklist.Asked);
        Assert.Empty(harness.Worklist.Read);
        Assert.Equal(0, harness.Detections.Keeps);
    }

    [Fact(DisplayName = "BR-CD-004: nothing is detected in spare time while something is being recorded")]
    public async Task NothingIsDetectedInSpareTimeWhileRecording()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        await harness.ReadAsync(0, "架空の物語");
        harness.Recording = true;

        SegmentDetectionLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.Recording, null), look);
        Assert.Empty(harness.Worklist.Read);
    }

    [Fact(DisplayName = "BR-CS-002: a recording is detected by the version in use and kept with how it ended: marked with its breaks, or nothing found")]
    public async Task ADetectionIsKeptForTheVersionInUse()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId plain = await harness.ReadAsync(1, "架空のクイズ");
        RecordingId withABreak = await harness.ReadAsync(0, "架空の物語", cuts: ABreak);
        SegmentDetectionJob job = harness.Job();

        SegmentDetectionLook first = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook second = await job.LookAsync(CancellationToken.None);

        RecordingDetection marked = harness.Detections.Of(withABreak, SegmentModelVersion.FirstNumber)!;
        RecordingDetection nothing = harness.Detections.Of(plain, SegmentModelVersion.FirstNumber)!;
        Assert.Same(marked, first.Detected);
        Assert.Same(nothing, second.Detected);
        Assert.Equal(SegmentDetectionState.Marked, marked.State);
        Assert.Equal(
            [(0d, 100d, SegmentKind.Programme), (100d, 160d, SegmentKind.Break), (160d, 300d, SegmentKind.Programme)],
            marked.Segments.Select(segment => (Math.Round(segment.Starts.TotalSeconds, 1), Math.Round(segment.Ends.TotalSeconds, 1), segment.Kind)));
        Assert.Equal(0.2, marked.CmShare, 3);
        Assert.Equal([100d, 130d, 160d], marked.Snaps.Select(snap => Math.Round(snap.TotalSeconds, 1)));
        Assert.Equal(LearningData.ExtractionVersion, marked.ExtractionVersionNumber);
        Assert.Equal((Now, Now), (marked.CatalogUntil, marked.DetectedAt));
        Assert.Equal(SegmentDetectionState.NothingFound, nothing.State);
        Assert.Equal(0, nothing.CmShare);
    }

    [Fact(DisplayName = "a recording matched at once is detected at once, even while something is being recorded, and the rest wait for spare time")]
    public async Task ARecordingMatchedAtOnceIsDetectedAtOnce()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId waiting = await harness.ReadAsync(7, "架空のクイズ");
        RecordingId matched = await harness.ReadAsync(0, "架空の物語", cuts: ABreak);
        harness.Recording = true;
        SegmentDetectionJob job = harness.Job();

        SegmentDetectionLook atOnce = await job.DetectMatchedAsync(matched, CancellationToken.None);
        SegmentDetectionLook inSpareTime = await job.LookAsync(CancellationToken.None);

        Assert.Null(atOnce.Verdict);
        Assert.Equal(matched, atOnce.Detected?.RecordingId);
        Assert.NotNull(harness.Detections.Of(matched, SegmentModelVersion.FirstNumber));
        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.Recording, null), inSpareTime);
        Assert.Null(harness.Detections.Of(waiting, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "a recording whose data still waits to be matched, or already detected since it was matched, is detected on neither path")]
    public async Task OnlyARecordingMatchedAndNotYetDetectedIsDetected()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId unmatched = await harness.ReadAsync(0, "架空の物語", matched: false);
        RecordingId detected = await harness.ReadAsync(1, "架空のクイズ");
        SegmentDetectionJob job = harness.Job();
        await job.DetectMatchedAsync(detected, CancellationToken.None);
        int kept = harness.Detections.Keeps;

        SegmentDetectionLook notMatched = await job.DetectMatchedAsync(unmatched, CancellationToken.None);
        SegmentDetectionLook again = await job.DetectMatchedAsync(detected, CancellationToken.None);
        SegmentDetectionLook look = await job.LookAsync(CancellationToken.None);

        Assert.Equal(new SegmentDetectionLook(null, null), notMatched);
        Assert.Equal(new SegmentDetectionLook(null, null), again);
        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.Spare, null), look);
        Assert.Equal(kept, harness.Detections.Keeps);
    }

    [Fact(DisplayName = "one recording is detected a look, the most recently started first, a recording thrown away is not detected in spare time, and a look with none waiting detects nothing")]
    public async Task OneRecordingIsDetectedALook()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(3, "架空のクイズ");
        RecordingId thrownAway = await harness.ReadAsync(2, "架空の天気");
        RecordingId newer = await harness.ReadAsync(1, "架空の物語");
        harness.Worklist.Kept.Remove(thrownAway);
        SegmentDetectionJob job = harness.Job();

        SegmentDetectionLook first = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook second = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook third = await job.LookAsync(CancellationToken.None);

        Assert.Equal(newer, first.Detected?.RecordingId);
        Assert.Equal(older, second.Detected?.RecordingId);
        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.Spare, null), third);
        Assert.Null(harness.Detections.Of(thrownAway, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CS-003: when the version in use changes, every recording still kept is detected again by it in spare time, and what the version before detected is kept")]
    public async Task ANewVersionInUseDetectsEveryRecordingAgain()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(2, "架空のクイズ");
        RecordingId newer = await harness.ReadAsync(1, "架空の物語", cuts: ABreak);
        SegmentDetectionJob job = harness.Job();
        await job.LookAsync(CancellationToken.None);
        await job.LookAsync(CancellationToken.None);

        harness.Versions.Active = HeldSegmentModelVersions.InUse(2, Now.AddMinutes(-1));
        SegmentDetectionLook first = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook second = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook third = await job.LookAsync(CancellationToken.None);

        Assert.Equal((newer, 2), (first.Detected?.RecordingId, first.Detected?.ModelVersion));
        Assert.Equal((older, 2), (second.Detected?.RecordingId, second.Detected?.ModelVersion));
        Assert.Null(third.Detected);
        Assert.Equal(4, harness.Detections.All().Count);
        Assert.NotNull(harness.Detections.Of(newer, SegmentModelVersion.FirstNumber));
        Assert.NotNull(harness.Detections.Of(older, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "BR-CD-007: once the first version of four kinds is in use, a recording detected before is detected again in spare time, the opening the catalog hears in it marked as the opening")]
    public async Task TheFirstVersionOfFourKindsDetectsTheOpeningAgain()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId recording = await harness.ReadAsync(1, "架空の物語");
        Opened(harness, recording, 20, 110);
        SegmentDetectionJob job = harness.Job();
        await job.LookAsync(CancellationToken.None);

        harness.Versions.Active = SegmentModelVersion.Rehydrate(2, SegmentModelVersionState.Active, false, SegmentModel.FirstOfFourKinds, Now.AddMinutes(-1), Now.AddMinutes(-1), null, null, null);
        SegmentDetectionLook again = await job.LookAsync(CancellationToken.None);

        Assert.Equal(SegmentDetectionState.NothingFound, harness.Detections.Of(recording, SegmentModelVersion.FirstNumber)!.State);
        RecordingDetection opened = harness.Detections.Of(recording, 2)!;
        Assert.Same(opened, again.Detected);
        Assert.Equal(SegmentDetectionState.Marked, opened.State);
        Assert.Equal(
            [(0d, 20d, SegmentKind.Programme), (20d, 110d, SegmentKind.Opening), (110d, 300d, SegmentKind.Programme)],
            opened.Segments.Select(segment => (Math.Round(segment.Starts.TotalSeconds, 1), Math.Round(segment.Ends.TotalSeconds, 1), segment.Kind)));
        Assert.True(opened.Segments[1].Probability >= SegmentSettingsStanding.DefaultSkipThreshold);
        Assert.Equal(0, opened.CmShare);
    }

    [Fact(DisplayName = "a recording matched again after it was detected is detected again, the detection taking the place of the one kept")]
    public async Task ARecordingMatchedAgainIsDetectedAgain()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId recording = await harness.ReadAsync(1, "架空の物語");
        SegmentDetectionJob job = harness.Job();
        await job.LookAsync(CancellationToken.None);

        harness.ReadAgain(recording);
        SegmentDetectionLook again = await job.LookAsync(CancellationToken.None);

        Assert.Equal(recording, again.Detected?.RecordingId);
        Assert.Equal(2, harness.Detections.Keeps);
        Assert.Single(harness.Detections.All());
        Assert.Equal(LearningData.ExtractionVersion + 1, harness.Detections.Of(recording, SegmentModelVersion.FirstNumber)!.ExtractionVersionNumber);
    }

    [Fact(DisplayName = "BR-CD-004: a recording detected before the catalog changed under it is detected again in spare time ahead of a later one whose catalog did not change")]
    public async Task ARecordingWhoseCatalogChangedIsDetectedAgain()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId changed = await harness.ReadAsync(2, "架空の物語");
        RecordingId unchanged = await harness.ReadAsync(1, "架空のクイズ");
        SegmentDetectionJob job = harness.Job();
        await job.LookAsync(CancellationToken.None);
        await job.LookAsync(CancellationToken.None);

        LearningExtraction record = harness.Records.Row(changed)!;
        Assert.True(record.CatalogChanged(Now.AddMinutes(1)));
        harness.Records.Hold(record);
        SegmentDetectionLook again = await job.LookAsync(CancellationToken.None);

        Assert.Equal(changed, again.Detected?.RecordingId);
        Assert.Equal(3, harness.Detections.Keeps);
    }

    [Fact(DisplayName = "BR-CD-013: a detection finished after learning was switched off is not kept, and the recording still waits")]
    public async Task ADetectionFinishedAfterLearningWasSwitchedOffIsNotKept()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId recording = await harness.ReadAsync(1, "架空の物語");
        harness.Worklist.WhileReading = _ => harness.LearningOff();

        SegmentDetectionLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.LearningOff, null), look);
        Assert.Equal(0, harness.Detections.Keeps);
        Assert.True(RecordingDetection.IsAwaited(harness.Records.Row(recording)!, null));
    }

    [Fact(DisplayName = "a recording whose detection fails is passed over until the app starts again, and the next one is detected")]
    public async Task ARecordingWhoseDetectionFailsIsPassedOver()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(2, "架空のクイズ");
        RecordingId unreadable = await harness.ReadAsync(1, "架空の物語");
        harness.Worklist.Unreadable.Add(unreadable);
        SegmentDetectionJob job = harness.Job();

        SegmentDetectionLook failed = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook next = await job.LookAsync(CancellationToken.None);
        SegmentDetectionLook after = await job.LookAsync(CancellationToken.None);

        Assert.Equal(new SegmentDetectionLook(SpareTimeVerdict.Spare, null), failed);
        Assert.Equal([unreadable], job.PassedOver);
        Assert.Equal(older, next.Detected?.RecordingId);
        Assert.Null(after.Detected);
        Assert.Null(harness.Detections.Of(unreadable, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "the running job detects a recording matched at once as soon as it is told, without waiting for its next look")]
    public async Task TheRunningJobDetectsARecordingAsSoonAsItIsTold()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId waiting = await harness.ReadAsync(7, "架空のクイズ");
        RecordingId matched = await harness.ReadAsync(0, "架空の物語");
        harness.Recording = true;
        SegmentDetectionJob job = harness.Job(new SegmentDetectionSettings { BeforeFirstLook = TimeSpan.FromHours(1), BetweenLooks = TimeSpan.FromHours(1) });

        await job.StartAsync(CancellationToken.None);
        harness.MatchedAtOnce.Tell(matched);
        await Eventually.Happens(() => harness.Detections.Of(matched, SegmentModelVersion.FirstNumber) is not null, "the recording told was never detected");
        await job.StopAsync(CancellationToken.None);

        Assert.Null(harness.Detections.Of(waiting, SegmentModelVersion.FirstNumber));
    }

    [Fact(DisplayName = "the running job detects the recordings waiting for spare time on its own looks, the next soon after a detection")]
    public async Task TheRunningJobDetectsTheWaitingRecordingsOnItsOwnLooks()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(3, "架空のクイズ");
        RecordingId newer = await harness.ReadAsync(1, "架空の物語");
        SegmentDetectionJob job = harness.Job(new SegmentDetectionSettings
        {
            BeforeFirstLook = TimeSpan.FromMilliseconds(10),
            BetweenLooks = TimeSpan.FromHours(1),
            AfterADetection = TimeSpan.FromMilliseconds(10),
        });

        await job.StartAsync(CancellationToken.None);
        await Eventually.Happens(
            () => harness.Detections.Of(older, SegmentModelVersion.FirstNumber) is not null && harness.Detections.Of(newer, SegmentModelVersion.FirstNumber) is not null,
            "the recordings waiting for spare time were never both detected");
        await job.StopAsync(CancellationToken.None);

        Assert.Equal(newer, harness.Worklist.Read[0]);
    }

    [Fact(DisplayName = "the counting of a detection runs on the lowest priority scheduler: the logo, the readings and the detection itself")]
    public async Task TheCountingRunsOnTheLowestPriorityScheduler()
    {
        using SegmentDetectionHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId recording = await harness.ReadAsync(0, "架空の物語");
        long before = harness.Lowest.Ran;

        await harness.Job().DetectMatchedAsync(recording, CancellationToken.None);

        Assert.Equal(before + 3, harness.Lowest.Ran);
    }

    /// <summary>
    /// Has the catalog hear an opening in <paramref name="recording"/> from <paramref name="from"/> to
    /// <paramref name="to"/> seconds, and in an earlier episode.
    /// </summary>
    private static void Opened(SegmentDetectionHarness harness, RecordingId recording, double from, double to)
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence heard = CatalogOccurrence.Found(
            id,
            recording,
            new SoundSpan(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to)),
            harness.Records.Row(recording)!.Programme,
            0.05,
            Now);

        harness.Catalog.Entries.Add(CatalogEntry.Rehydrate(
            id,
            CatalogKind.Opening,
            0.8,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(to - from),
            new byte[sizeof(uint) * 4],
            2,
            1,
            1,
            Now,
            Now,
            Now,
            CatalogKinds.Version));
        harness.Catalog.Occurrences.Add(heard);
    }
}
