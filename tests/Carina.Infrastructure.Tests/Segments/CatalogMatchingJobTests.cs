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

    [Fact(DisplayName = "BR-CD-004: a recording followed to its end and marked as matched at once is told on to be detected at once, and one matched in spare time is not")]
    public async Task ARecordingMatchedAtOnceIsToldOnToBeDetected()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId waiting = await harness.ReadAsync(Sound(1), 7, "架空のクイズ");
        RecordingId followed = await harness.ReadAsync(Sound(2), 0, "架空の物語");
        CatalogMatchingJob job = harness.Job();

        await job.MatchFollowedAsync(followed, CancellationToken.None);
        bool toldOnce = harness.MatchedAtOnce.TryTake(out RecordingId? told);
        CatalogMatchingLook inSpareTime = await job.LookAsync(CancellationToken.None);

        Assert.True(toldOnce);
        Assert.Equal(followed, told);
        Assert.Equal(waiting, inSpareTime.Matched?.Recording);
        Assert.False(harness.MatchedAtOnce.TryTake(out _));
    }

    [Fact(DisplayName = "a recording followed to its end that is not matched at once is not told on to be detected")]
    public async Task ARecordingNotMatchedAtOnceIsNotToldOn()
    {
        using CatalogMatchingHarness harness = new();
        RecordingId followed = await harness.ReadAsync(Sound(1), 0, "架空の物語");
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook off = await job.MatchFollowedAsync(followed, CancellationToken.None);

        Assert.Equal(SpareTimeVerdict.LearningOff, off.Verdict);
        Assert.False(harness.MatchedAtOnce.TryTake(out _));
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

    [Fact(DisplayName = "the running job matches the recordings waiting for spare time on its own looks, the next soon after a match")]
    public async Task TheRunningJobMatchesTheWaitingRecordingsOnItsOwnLooks()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId older = await harness.ReadAsync(Sound(1), 3, "架空のクイズ");
        RecordingId newer = await harness.ReadAsync(Sound(2), 1, "架空の物語");
        CatalogMatchingJob job = harness.Job(new CatalogMatchingSettings
        {
            BeforeFirstLook = TimeSpan.FromMilliseconds(10),
            BetweenLooks = TimeSpan.FromHours(1),
            AfterAMatch = TimeSpan.FromMilliseconds(10),
        });

        await job.StartAsync(CancellationToken.None);
        await Eventually.Happens(
            () => harness.Records.Row(older)?.AwaitsMatching is false && harness.Records.Row(newer)?.AwaitsMatching is false,
            "the recordings waiting for spare time were never both matched");
        await job.StopAsync(CancellationToken.None);

        Assert.Equal(newer, harness.Worklist.Heard[0]);
    }

    [Fact(DisplayName = "the counting of a match runs on the lowest priority scheduler: making the matcher, the entries, each other recording, the recording itself and the catalog change")]
    public async Task TheCountingOfAMatchRunsOnTheLowestPriorityScheduler()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        uint[] earlier = Sound(1);
        await harness.ReadAsync(earlier, 7, "架空のクイズ");
        RecordingId heard = await harness.ReadAsync(Sharing(Sound(2), earlier, 5_000, 9_000, Advert), 0, "架空の物語");

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(heard, look.Matched?.Recording);
        Assert.Equal(5, harness.Lowest.Ran);
    }

    [Fact(DisplayName = "the counting runs on a thread of its own at the lowest priority, and on Linux at the highest nice value")]
    public async Task TheCountingRunsAtTheLowestPriority()
    {
        using LowestPriorityScheduler lowest = new();

        (int Thread, ThreadPriority Priority, string? Nice) seen = await lowest.Run(
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.Priority, NiceOfThisThread()),
            CancellationToken.None);

        Assert.NotEqual(Environment.CurrentManagedThreadId, seen.Thread);
        Assert.Equal(1, lowest.Ran);
        Assert.Equal(ThreadPriority.Lowest, seen.Priority);
        Assert.Equal(OperatingSystem.IsLinux() ? LowestPriorityScheduler.LowestNice : null, await lowest.Lowered);
        Assert.Equal(OperatingSystem.IsLinux() ? "19" : null, seen.Nice);
    }

    [Fact(DisplayName = "BR-CD-005: in spare time with no recording waiting, an entry estimated before the rows for openings and endings is estimated again, once")]
    public async Task AnEntryEstimatedBeforeTheThemesIsEstimatedAgainOnce()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        CatalogEntry entry = await OutdatedAsync(harness);
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook first = await job.LookAsync(CancellationToken.None);
        CatalogMatchingLook second = await job.LookAsync(CancellationToken.None);

        Assert.Same(entry, first.Reestimated);
        Assert.Null(first.Matched);
        Assert.Equal((CatalogKind.Opening, 0.8, CatalogDecision.Pattern), (entry.Kind, entry.Confidence, entry.DecidedBy));
        Assert.Equal(CatalogKinds.Version, entry.KindsVersion);
        Assert.Equal(Now, entry.UpdatedAt);
        Assert.Equal(new CatalogMatchingLook(SpareTimeVerdict.Spare, null, false), second);
        Assert.Equal(1, harness.Catalog.Keeps);
    }

    [Fact(DisplayName = "BR-CD-005: an entry of one series estimated again in spare time is read beside the stretches heard in its recordings, and carries on the opening it follows")]
    public async Task AnEntryEstimatedAgainCarriesOnTheOpeningItFollows()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        RecordingId[] weeks = [await harness.ReadAsync(Sound(12), 14, "架空の物語"), await harness.ReadAsync(Sound(11), 7, "架空の物語")];

        foreach (RecordingId week in weeks)
        {
            LearningExtraction record = harness.Records.Row(week)!;
            record.Matched(Now);
            harness.Records.Hold(record);
        }

        CatalogEntry head = Kept(harness, weeks, CatalogKind.Opening, 0.8, Into, Heard, CatalogKinds.Version);
        CatalogEntry phrase = Kept(harness, weeks, CatalogKind.ProgrammeFixture, 0.7, Into + Heard, TimeSpan.FromSeconds(13), CatalogKinds.FirstVersion);

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Same(phrase, look.Reestimated);
        Assert.Equal((CatalogKind.Opening, 0.8, CatalogDecision.Continuation), (phrase.Kind, phrase.Confidence, phrase.DecidedBy));
        Assert.Equal((CatalogKind.Opening, CatalogDecision.Pattern), (head.Kind, head.DecidedBy));
    }

    [Fact(DisplayName = "an entry put back after a change of links is estimated again in spare time, once")]
    public async Task AnEntryPutBackIsEstimatedAgainOnce()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        CatalogEntry entry = await OutdatedAsync(harness, CatalogKinds.Unsettled);
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook first = await job.LookAsync(CancellationToken.None);
        CatalogMatchingLook second = await job.LookAsync(CancellationToken.None);

        Assert.Same(entry, first.Reestimated);
        Assert.Equal((CatalogKind.Opening, CatalogKinds.Version), (entry.Kind, entry.KindsVersion));
        Assert.Null(second.Reestimated);
        Assert.Equal(1, harness.Catalog.Keeps);
    }

    [Fact(DisplayName = "BR-CD-004: no entry is estimated again while something is being recorded or while learning is off")]
    public async Task NoEntryIsEstimatedAgainOutsideSpareTime()
    {
        using CatalogMatchingHarness harness = new();
        CatalogEntry entry = await OutdatedAsync(harness);
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook off = await job.LookAsync(CancellationToken.None);
        await harness.LearningAsync(true);
        harness.Recording = true;
        CatalogMatchingLook recording = await job.LookAsync(CancellationToken.None);

        Assert.Equal(new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false), off);
        Assert.Equal(new CatalogMatchingLook(SpareTimeVerdict.Recording, null, false), recording);
        Assert.True(entry.AwaitsReestimation);
        Assert.Equal(0, harness.Catalog.Keeps);
    }

    [Fact(DisplayName = "a recording waiting to be matched is matched before an entry is estimated again")]
    public async Task ARecordingWaitingIsMatchedFirst()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        CatalogEntry entry = await OutdatedAsync(harness);
        RecordingId waiting = await harness.ReadAsync(Sound(3), 0, "架空のクイズ");
        CatalogMatchingJob job = harness.Job();

        CatalogMatchingLook first = await job.LookAsync(CancellationToken.None);
        CatalogMatchingLook second = await job.LookAsync(CancellationToken.None);

        Assert.Equal(waiting, first.Matched?.Recording);
        Assert.Null(first.Reestimated);
        Assert.Same(entry, second.Reestimated);
    }

    [Fact(DisplayName = "BR-CD-013: an entry estimated again after learning was switched off is not kept")]
    public async Task AnEstimateFinishedAfterLearningWasSwitchedOffIsNotKept()
    {
        using CatalogMatchingHarness harness = new();
        await harness.LearningAsync(true);
        await OutdatedAsync(harness);
        harness.Catalog.WhileReading = () => harness.LearningAsync(false).GetAwaiter().GetResult();

        CatalogMatchingLook look = await harness.Job().LookAsync(CancellationToken.None);

        Assert.Equal(new CatalogMatchingLook(SpareTimeVerdict.LearningOff, null, false), look);
        Assert.Equal(0, harness.Catalog.Keeps);
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

    private static readonly TimeSpan Into = TimeSpan.FromSeconds(35);

    private static readonly TimeSpan Heard = TimeSpan.FromSeconds(89);

    /// <summary>
    /// An entry kept from before the rows for openings and endings, of no kind yet: a minute and a half heard
    /// near the start of two weekly episodes, both matched already.
    /// </summary>
    private static CatalogEntry Kept(
        CatalogMatchingHarness harness,
        RecordingId[] recordings,
        CatalogKind kind,
        double confidence,
        TimeSpan into,
        TimeSpan length,
        int kindsVersion)
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            .. recordings.Select(recording => harness.Records.Row(recording)!.Programme).Select((programme, at) => CatalogOccurrence.Rehydrate(
                harness.Catalog.Occurrences.Count + at + 1,
                id,
                recordings[at],
                into,
                into + length,
                programme.NetworkId,
                programme.ServiceId,
                programme.SeriesName,
                programme.RecordingStartedAt,
                0.05f,
                null,
                Now)),
        ];
        CatalogEntry entry = CatalogEntry.Rehydrate(
            id,
            kind,
            confidence,
            CatalogDecision.Pattern,
            length,
            CatalogFingerprint.Write(Sound(1).AsSpan(0, (int)(length / SegmentMatcher.Frame))),
            heard.Length,
            1,
            1,
            heard.Min(stretch => stretch.RecordingStartedAt),
            heard.Max(stretch => stretch.RecordingStartedAt),
            Now.AddDays(-1),
            kindsVersion);

        harness.Catalog.Entries.Add(entry);
        harness.Catalog.Occurrences.AddRange(heard);

        return entry;
    }

    private static async Task<CatalogEntry> OutdatedAsync(CatalogMatchingHarness harness, int kindsVersion = CatalogKinds.FirstVersion)
    {
        CatalogEntryId id = CatalogEntryId.New();
        List<CatalogOccurrence> heard = [];

        foreach (int week in new[] { 2, 1 })
        {
            RecordingId recording = await harness.ReadAsync(Sound(10 + week), 7 * week, "架空の物語");
            LearningExtraction record = harness.Records.Row(recording)!;
            record.Matched(Now);
            harness.Records.Hold(record);
            heard.Add(CatalogOccurrence.Rehydrate(
                heard.Count + 1,
                id,
                recording,
                Into,
                Into + Heard,
                record.Programme.NetworkId,
                record.Programme.ServiceId,
                record.Programme.SeriesName,
                record.Programme.RecordingStartedAt,
                0.05f,
                null,
                Now));
        }

        CatalogEntry entry = CatalogEntry.Rehydrate(
            id,
            CatalogKind.Unknown,
            null,
            null,
            Heard,
            CatalogFingerprint.Write(Sound(1).AsSpan(0, (int)(Heard / SegmentMatcher.Frame))),
            heard.Count,
            1,
            1,
            heard[0].RecordingStartedAt,
            heard[^1].RecordingStartedAt,
            Now.AddDays(-1),
            kindsVersion);

        harness.Catalog.Entries.Add(entry);
        harness.Catalog.Occurrences.AddRange(heard);

        return entry;
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
