using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.CatalogMatchingHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class CatalogMatchingJobTests
{
    private const int Advert = 938;

    [Fact(DisplayName = "BR-CD-013: while learning is off no recording is matched, and nothing is read or kept")]
    public async Task NothingIsMatchedWhileLearningIsOff()
    {
        using CatalogMatchingHarness harness = new();
        await harness.ReadAsync(Sound(1), 0, "架空の物語");

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(SpareTimeVerdict.LearningOff, look.Verdict);
        Assert.Null(look.Matched);
        Assert.Equal(0, harness.Worklist.Asked);
        Assert.Empty(harness.Worklist.Heard);
        Assert.Equal(0, harness.Catalog.Keeps);
    }

    [Fact(DisplayName = "BR-CD-004: nothing is matched while something is being recorded")]
    public async Task NothingIsMatchedWhileRecording()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        await harness.ReadAsync(Sound(1), 0, "架空の物語");
        harness.Recording = true;

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(SpareTimeVerdict.Recording, look.Verdict);
        Assert.Null(look.Matched);
        Assert.Empty(harness.Worklist.Heard);
    }

    [Fact(DisplayName = "a recording read to its end is matched against a recent one, the shared piece starting an entry with a stretch in each")]
    public async Task ARecordingIsMatchedAgainstARecentOne()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        RecordingId before = await harness.ReadAsync(earlier, 7, "架空のクイズ");
        RecordingId heard = await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(heard, look.Matched?.Recording);
        Assert.True(look.Marked);
        CatalogEntry entry = Assert.Single(harness.Catalog.Entries);
        Assert.Equal(CatalogKind.Cm, entry.Kind);
        Assert.Equal(0.9, entry.Confidence);
        Assert.Equal([before, heard], harness.Catalog.Occurrences.Select(occurrence => occurrence.RecordingId).OrderBy(id => id.Equals(heard)));
        Assert.False(harness.Records.Row(heard)!.AwaitsMatching);
        Assert.Equal(Now, harness.Records.Row(heard)!.MatchedAt);
    }

    [Fact(DisplayName = "one recording is matched a look, the most recently started first, and a look with none waiting matches nothing")]
    public async Task OneRecordingIsMatchedALook()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(Sound(1), 3, "架空のクイズ");
        RecordingId newer = await harness.ReadAsync(Sound(2), 1, "架空の物語");
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook first = await job.LookAsync(CancellationToken.None);
        CatalogMatchingLook second = await job.LookAsync(CancellationToken.None);
        CatalogMatchingLook third = await job.LookAsync(CancellationToken.None);

        Assert.Equal(newer, first.Matched?.Recording);
        Assert.Equal(older, second.Matched?.Recording);
        Assert.Equal(SpareTimeVerdict.Spare, third.Verdict);
        Assert.Null(third.Matched);
        Assert.Equal(2, harness.Catalog.Keeps);
    }

    [Fact(DisplayName = "the shared piece found once is not found again when the other recording is matched, and a later recording joins its entry")]
    public async Task APieceFoundOnceIsNotFoundAgain()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] source = Sound(1);
        await harness.ReadAsync(Sharing(Sound(3), source, 1_000, 9_000, Advert), 14, "架空のクイズ");
        await harness.ReadAsync(Sharing(Sound(2), source, 5_000, 9_000, Advert), 7, "架空の物語");
        CatalogMatchingJob job = harness.Job();

        await job.LookAsync(CancellationToken.None);
        await job.LookAsync(CancellationToken.None);
        RecordingId later = await harness.ReadAsync(Sharing(Sound(4), source, 12_000, 9_000, Advert), 0, "架空の天気", updatedMinutesAgo: 1);
        CatalogMatchingLook look = await job.LookAsync(CancellationToken.None);

        CatalogEntry entry = Assert.Single(harness.Catalog.Entries);
        Assert.Equal(later, look.Matched?.Recording);
        Assert.Equal(3, harness.Catalog.Occurrences.Count);
        Assert.Contains(harness.Catalog.Occurrences, occurrence => occurrence.RecordingId.Equals(later) && occurrence.EntryId.Equals(entry.Id));
    }

    [Fact(DisplayName = "a recording started more than sixty days apart is not matched against")]
    public async Task ARecordingFarApartIsNotMatchedAgainst()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        RecordingId longAgo = await harness.ReadAsync(earlier, 61, "架空のクイズ");
        LearningExtraction kept = harness.Records.Row(longAgo)!;
        kept.Matched(Now);
        harness.Records.Hold(kept);
        await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.NotNull(look.Matched);
        Assert.DoesNotContain(longAgo, harness.Worklist.Heard);
        Assert.Empty(harness.Catalog.Entries);
    }

    [Fact(DisplayName = "BR-CD-013: a match finished after learning was switched off is not kept, and the recording still waits")]
    public async Task AMatchFinishedAfterLearningWasSwitchedOffIsNotKept()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        await harness.ReadAsync(earlier, 7, "架空のクイズ");
        RecordingId heard = await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");
        harness.Worklist.WhileHearing = _ => harness.LearningAsync(false).GetAwaiter().GetResult();

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(SpareTimeVerdict.LearningOff, look.Verdict);
        Assert.Equal(0, harness.Catalog.Keeps);
        Assert.True(harness.Records.Row(heard)!.AwaitsMatching);
    }

    [Fact(DisplayName = "a record that changed while it was matched keeps waiting, and is matched again without adding a stretch twice")]
    public async Task ARecordThatChangedMeanwhileKeepsWaiting()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        await harness.ReadAsync(earlier, 7, "架空のクイズ");
        RecordingId heard = await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");
        CatalogMatchingJob job = harness.Job();
        harness.Worklist.WhileHearing = recording => Reread(harness, recording, heard);

        CatalogMatchingLook first = await job.LookAsync(CancellationToken.None);
        harness.Worklist.WhileHearing = null;
        CatalogMatchingLook second = await job.LookAsync(CancellationToken.None);

        Assert.False(first.Marked);
        Assert.True(second.Marked);
        Assert.Equal(heard, second.Matched?.Recording);
        Assert.Single(harness.Catalog.Entries);
        Assert.Equal(2, harness.Catalog.Occurrences.Count);
    }

    [Fact(DisplayName = "a recording followed to its end is matched at once, even while something is being recorded, and the rest wait for spare time")]
    public async Task ARecordingFollowedToItsEndIsMatchedAtOnce()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        RecordingId waiting = await harness.ReadAsync(earlier, 7, "架空のクイズ");
        RecordingId followed = await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");
        harness.Recording = true;
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook atOnce = await job.MatchFollowedAsync(followed, CancellationToken.None);
        CatalogMatchingLook inSpareTime = await job.LookAsync(CancellationToken.None);

        Assert.Null(atOnce.Verdict);
        Assert.Equal(followed, atOnce.Matched?.Recording);
        Assert.True(atOnce.Marked);
        Assert.Single(harness.Catalog.Entries);
        Assert.Equal(SpareTimeVerdict.Recording, inSpareTime.Verdict);
        Assert.Null(inSpareTime.Matched);
        Assert.True(harness.Records.Row(waiting)!.AwaitsMatching);
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off a recording followed to its end is not matched, and is matched in spare time once learning is on")]
    public async Task ARecordingFollowedWhileLearningIsOffWaitsForSpareTime()
    {
        using CatalogMatchingHarness harness = new();
        RecordingId followed = await harness.ReadAsync(Sound(1), 0, "架空の物語");
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook off = await job.MatchFollowedAsync(followed, CancellationToken.None);
        int heardWhileOff = harness.Worklist.Heard.Count;
        await harness.LearningAsync(true);
        CatalogMatchingLook later = await job.LookAsync(CancellationToken.None);

        Assert.Equal(SpareTimeVerdict.LearningOff, off.Verdict);
        Assert.Null(off.Matched);
        Assert.Equal(0, heardWhileOff);
        Assert.Equal(followed, later.Matched?.Recording);
    }

    [Fact(DisplayName = "a recording followed to its end whose data was not read to the end, or that is already matched, is not matched at once")]
    public async Task OnlyARecordReadToTheEndAndWaitingIsMatchedAtOnce()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId cutShort = await harness.ReadAsync(Sound(1), 0, "架空の物語");
        RecordingId matched = await harness.ReadAsync(Sound(2), 1, "架空のクイズ");
        LearningExtraction partway = harness.Records.Row(cutShort)!;
        LearningExtraction done = harness.Records.Row(matched)!;
        partway.Outdate(new ExtractionVersion(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile), Now);
        partway.Read(new ExtractionVersion(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile), Now);
        partway.FinishPartway(Now);
        done.Matched(Now);
        harness.Records.Hold(partway);
        harness.Records.Hold(done);
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook partial = await job.MatchFollowedAsync(cutShort, CancellationToken.None);
        CatalogMatchingLook again = await job.MatchFollowedAsync(matched, CancellationToken.None);

        Assert.Equal(new CatalogMatchingLook(null, null, false), partial);
        Assert.Equal(new CatalogMatchingLook(null, null, false), again);
        Assert.Empty(harness.Worklist.Heard);
        Assert.True(harness.Records.Row(cutShort)!.AwaitsMatching);
    }

    [Fact(DisplayName = "the running job takes a recording followed to its end as soon as it is told, without waiting for its next look")]
    public async Task TheRunningJobTakesAFollowedRecordingAsSoonAsItIsTold()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId waiting = await harness.ReadAsync(Sound(1), 7, "架空のクイズ");
        RecordingId followed = await harness.ReadAsync(Sound(2), 0, "架空の物語");
        harness.Recording = true;
        CatalogMatchingJob job = harness.Job(new CatalogMatchingSettings { BeforeFirstLook = TimeSpan.FromHours(1), BetweenLooks = TimeSpan.FromHours(1) });

        await job.StartAsync(CancellationToken.None);
        harness.Followed.Tell(followed);
        await Eventually.Happens(() => harness.Records.Row(followed)?.AwaitsMatching is false, "the recording told was never matched");
        await job.StopAsync(CancellationToken.None);

        Assert.True(harness.Records.Row(waiting)!.AwaitsMatching);
        Assert.Equal(followed, harness.Worklist.Heard[0]);
    }

    [Fact(DisplayName = "the counting runs on a thread of its own at the lowest priority, and on Linux at the highest nice value")]
    public async Task TheCountingRunsAtTheLowestPriority()
    {
        using LowestPriorityScheduler lowest = new();

        (int Thread, ThreadPriority Priority, string? Nice) seen = await lowest.Run(
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.Priority, NiceOfThisThread()),
            CancellationToken.None);

        Assert.NotEqual(Environment.CurrentManagedThreadId, seen.Thread);
        Assert.Equal(ThreadPriority.Lowest, seen.Priority);
        Assert.Equal(OperatingSystem.IsLinux() ? LowestPriorityScheduler.LowestNice : null, await lowest.Lowered);
        Assert.Equal(OperatingSystem.IsLinux() ? "19" : null, seen.Nice);
    }

    private static string? NiceOfThisThread()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        string stat = File.ReadAllText("/proc/thread-self/stat");

        return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[16];
    }

    private static void Reread(CatalogMatchingHarness harness, RecordingId recording, RecordingId heard)
    {
        if (!recording.Equals(heard) || harness.Records.Row(heard) is not { State: LearningExtractionState.Done } kept)
        {
            return;
        }

        ExtractionVersion newer = new(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile);

        kept.Outdate(newer, Now.AddMinutes(-2));
        kept.Read(newer, Now.AddMinutes(-2));
        kept.Finish(Now.AddMinutes(-1));
        harness.Records.Hold(kept);
    }
}
