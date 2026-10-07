using Carina.Domain.Captions;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.Infrastructure.Tests.Reservations;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningBacklogJobTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly LearningBacklogHarness harness = new();

    public static TheoryData<string> Occupied => ["learning off", "recording", "watching", "a reservation soon"];

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "in spare time the most recently started recording waiting is read from the head of its file to the end, its record reading")]
    public async Task InSpareTimeTheNewestRecordingWaitingIsRead()
    {
        await harness.LearningAsync(true);
        harness.Ended(90, 7601);
        Recording newest = harness.Ended(40, 7602);
        harness.Worklist.Ends[newest.Id] = Now.AddMinutes(25);
        LearningBacklogJob job = harness.Job();

        LearningBacklogLook look = await job.LookAsync(Cancel);

        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(newest.Id));
        Assert.Equal((LearningExtractionState.Reading, ExtractionVersion.Current), (record.State, record.Version));
        Assert.Equal(ProgrammeCopy.Of(newest, Now.AddMinutes(25)), record.Programme);
        Assert.Equal((SpareTimeVerdict.Spare, newest.Id, newest.Id), (look.Verdict, look.Began, look.Reading));
        await Eventually.Happens(() => !harness.Reader.Asked.IsEmpty, "the reading never began");
        FollowedRecording read = Assert.Single(harness.Reader.Asked);
        Assert.Equal((newest.Id, harness.Source(newest), newest.ServiceId), (read.Id, read.Source, read.Service));
        Assert.Equal((LearningExtractionState.Reading, true), (read.Held, read.HasEnded));
    }

    [Fact(DisplayName = "only one recording is read at a time, and the next begins once the one before has settled")]
    public async Task OnlyOneRecordingIsReadAtATime()
    {
        await harness.LearningAsync(true);
        Recording older = harness.Ended(90, 7603);
        Recording newer = harness.Ended(40, 7604);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);

        LearningBacklogLook meanwhile = await job.LookAsync(Cancel);

        Assert.Equal((null, newer.Id), (meanwhile.Began, meanwhile.Reading));
        Assert.Null(harness.Records.Row(older.Id));

        harness.Reader.Release(newer.Id);
        await LetGoAsync(job, newer);

        Assert.Equal(LearningExtractionState.Done, harness.Records.Row(newer.Id)?.State);
        Assert.Equal(older.Id, job.Reading);
        Assert.Equal(LearningExtractionState.Reading, harness.Records.Row(older.Id)?.State);
        Assert.Single(harness.Records.All(), record => record.State is LearningExtractionState.Reading);
    }

    [Fact(DisplayName = "a recording is not read while another is recorded as being read, and nothing is read twice at once")]
    public async Task ARecordingIsNotReadWhileAnotherIs()
    {
        await harness.LearningAsync(true);
        Recording elsewhere = harness.Ended(120, 7605);
        harness.Records.Hold(Left(elsewhere, LearningExtractionState.Done));
        Recording waiting = harness.Ended(30, 7606);
        harness.Records.MovesBeforeSaving = id =>
        {
            if (id.Equals(waiting.Id) && harness.Records.Row(elsewhere.Id)?.State is LearningExtractionState.Done)
            {
                harness.Records.Hold(Left(elsewhere, LearningExtractionState.Reading));
            }

            return false;
        };
        LearningBacklogJob job = harness.Job();

        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Null(look.Began);
        Assert.Null(job.Reading);
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(waiting.Id)?.State);
        Assert.Single(harness.Records.All(), record => record.State is LearningExtractionState.Reading);
        Assert.Empty(harness.Reader.Asked);
    }

    [Fact(DisplayName = "a record left reading while nothing is read is put back to wait at the next look, and its recording read again")]
    public async Task ARecordLeftReadingWhileNothingIsReadIsPutBack()
    {
        await harness.LearningAsync(true);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        Recording left = harness.Ended(30, 7628);
        harness.Records.Hold(Left(left, LearningExtractionState.Reading));

        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Equal((1, left.Id), (look.Recovered, look.Began));
        Assert.Equal(LearningExtractionState.Reading, harness.Records.Row(left.Id)?.State);
        Assert.Empty(harness.Records.Row(left.Id)!.Gaps);
    }

    [Fact(DisplayName = "a reading stopped by switching learning off is held until its record is put back to wait, though the first write fails")]
    public async Task AStoppedReadingIsHeldUntilItsRecordIsWritten()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7633);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => harness.Records.Row(recording.Id)?.ReadThrough == HeldReader.ReadsAtOnce, "the reading never read");
        await harness.LearningAsync(false);
        harness.Records.FailsSaving = _ => true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.LookAsync(Cancel));

        Assert.Equal(recording.Id, job.Reading);
        Assert.Equal(LearningExtractionState.Reading, harness.Records.Row(recording.Id)?.State);

        harness.Records.FailsSaving = null;
        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Equal((recording.Id, null), (look.Stopped, job.Reading));
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(recording.Id)?.State);
        Assert.Equal([recording.Id], harness.Reader.Stopped);
    }

    [Fact(DisplayName = "a reading that ended leaving its record reading is held until its record is taken as failed, though the first write fails")]
    public async Task AReadingLeftUnsettledIsHeldUntilItsRecordIsWritten()
    {
        await harness.LearningAsync(true);
        harness.Reader.LeavesItsRecord = true;
        Recording recording = harness.Ended(40, 7634);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => harness.Records.Row(recording.Id)?.ReadThrough == HeldReader.ReadsAtOnce, "the reading never read");
        harness.Records.FailsSaving = _ => true;
        harness.Reader.Release(recording.Id);

        await Eventually.Yields(
            async () => await RefusedAsync(job),
            refused => refused,
            _ => "the look wrote nothing",
            "the ended reading was never settled");

        Assert.Equal(recording.Id, job.Reading);
        Assert.Equal(LearningExtractionState.Reading, harness.Records.Row(recording.Id)?.State);

        harness.Records.FailsSaving = null;
        await harness.LearningAsync(false);
        await job.LookAsync(Cancel);

        Assert.Null(job.Reading);
        Assert.Equal(
            (LearningExtractionState.Failed, LearningBacklogJob.LeftUnsettled),
            (harness.Records.Row(recording.Id)?.State, harness.Records.Row(recording.Id)?.Failure?.Reason));
    }

    [Theory(DisplayName = "outside spare time nothing begins: with learning off, while recording, while watching, or with a reservation within thirty minutes")]
    [MemberData(nameof(Occupied))]
    public async Task OutsideSpareTimeNothingBegins(string occupied)
    {
        await harness.LearningAsync(occupied is not "learning off");
        Recording ended = harness.Ended(40, 7607);
        SpareTimeVerdict expected = await OccupyAsync(occupied);

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(expected, look.Verdict);
        Assert.Null(look.Began);
        Assert.Null(harness.Records.Row(ended.Id));
        Assert.Empty(harness.Reader.Asked);
    }

    [Fact(DisplayName = "a reservation further away than thirty minutes, its margin counted, leaves spare time")]
    public async Task AReservationFurtherAwayLeavesSpareTime()
    {
        await harness.LearningAsync(true);
        Recording ended = harness.Ended(40, 7608);
        await harness.Reservations.AddAsync(ReservationFixtures.Planned(startAt: Now.AddMinutes(45), marginBefore: Margin.OfSeconds(10 * 60)), Cancel);
        await harness.Reservations.AddAsync(ReservationFixtures.Planned(startAt: Now.AddHours(-3), endAt: Now.AddHours(-2)), Cancel);

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(ended.Id, look.Began);
    }

    [Fact(DisplayName = "a recording begun is read to its end though recording starts meanwhile, and the next waits for spare time again")]
    public async Task ARecordingBegunIsReadToItsEndThoughRecordingStarts()
    {
        await harness.LearningAsync(true);
        Recording older = harness.Ended(90, 7609);
        Recording newer = harness.Ended(40, 7610);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        harness.BeingRecorded(7611);
        harness.Watching.Anyone = true;

        LearningBacklogLook meanwhile = await job.LookAsync(Cancel);

        Assert.Equal((null, newer.Id), (meanwhile.Stopped, meanwhile.Reading));
        Assert.Empty(harness.Reader.Stopped);

        harness.Reader.Release(newer.Id);
        LearningBacklogLook after = await LetGoAsync(job, newer);

        Assert.Equal(LearningExtractionState.Done, harness.Records.Row(newer.Id)?.State);
        Assert.Equal((SpareTimeVerdict.Recording, null), (after.Verdict, after.Began));
        Assert.Null(harness.Records.Row(older.Id));
    }

    [Fact(DisplayName = "switching learning off stops the reading within a look, keeps what it wrote, and puts its record back to wait")]
    public async Task SwitchingLearningOffStopsTheReading()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7612);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => !harness.Reader.Asked.IsEmpty, "the reading never began");
        await harness.Data.KeepAsync(Written(recording), Cancel);

        await harness.LearningAsync(false);
        LearningBacklogLook look = await job.LookAsync(Cancel);

        Assert.Equal((recording.Id, null, SpareTimeVerdict.LearningOff), (look.Stopped, look.Reading, look.Verdict));
        Assert.Equal([recording.Id], harness.Reader.Stopped);
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(recording.Id)?.State);
        Assert.Single(harness.Data.Of(recording.Id));
        Assert.Null(job.Reading);

        await harness.LearningAsync(true);

        Assert.Equal(recording.Id, (await job.LookAsync(Cancel)).Began);
    }

    [Fact(DisplayName = "a recording whose row goes while it is read is read no further, and its record keeps what was read, partway")]
    public async Task ARecordingWhoseRowGoesEndsPartway()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7613);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => harness.Records.Row(recording.Id)?.ReadThrough == HeldReader.ReadsAtOnce, "the reading never read");

        harness.Worklist.Recordings.Remove(recording);
        await job.LookAsync(Cancel);

        await Eventually.Happens(() => harness.Records.Row(recording.Id)?.State is LearningExtractionState.Partial, "the reading never stopped partway");
        Assert.Equal(HeldReader.ReadsAtOnce, harness.Records.Row(recording.Id)?.ReadThrough);
        Assert.True(Assert.Single(harness.Reader.Asked).HasGone);
    }

    [Fact(DisplayName = "a recording whose file goes while it is read is read no further, and its record keeps what was read, partway")]
    public async Task ARecordingWhoseFileGoesEndsPartway()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7614);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => !harness.Reader.Asked.IsEmpty, "the reading never began");

        File.Delete(harness.Source(recording));
        await job.LookAsync(Cancel);

        await Eventually.Happens(() => harness.Records.Row(recording.Id)?.State is LearningExtractionState.Partial, "the reading never stopped partway");
    }

    [Fact(DisplayName = "the recording's file is still there, as it was, once it has been read")]
    public async Task TheFileIsStillThereOnceRead()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Ended(40, 7615);
        byte[] before = await File.ReadAllBytesAsync(harness.Source(recording));
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);

        harness.Reader.Release(recording.Id);
        await LetGoAsync(job, recording);

        Assert.Equal(LearningExtractionState.Done, harness.Records.Row(recording.Id)?.State);
        Assert.Equal(before, await File.ReadAllBytesAsync(harness.Source(recording)));
    }

    [Fact(DisplayName = "a recording whose file is out of reach is passed over and keeps waiting, and the next is read")]
    public async Task ARecordingOutOfReachIsPassedOver()
    {
        await harness.LearningAsync(true);
        Recording reachable = harness.Ended(90, 7616);
        Recording withoutAFile = harness.Ended(40, 7617, withAFile: false);
        Recording waitingWithoutAFile = harness.Ended(30, 7618, withAFile: false);
        harness.Records.Hold(LearningExtraction.Waiting(waitingWithoutAFile.Id, ProgrammeCopy.Of(waitingWithoutAFile, null), Now));
        Recording unmounted = harness.Ended(20, 7619, root: Unmounted);

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((reachable.Id, 2), (look.Began, look.OutOfReach));
        Assert.Null(harness.Records.Row(withoutAFile.Id));
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(waitingWithoutAFile.Id)?.State);
        Assert.Null(harness.Records.Row(unmounted.Id));
    }

    [Fact(DisplayName = "more recordings out of reach than a look takes at once are passed over, and an older one whose file is there is read")]
    public async Task MoreRecordingsOutOfReachThanALookTakesArePassedOver()
    {
        await harness.LearningAsync(true);
        Recording older = harness.Ended(600, 7630);

        for (int unreachable = 0; unreachable <= LearningBacklogSettings.Default.AtMostALook; unreachable++)
        {
            harness.Ended(500 - unreachable, 7700 + unreachable, withAFile: false);
        }

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((older.Id, LearningBacklogSettings.Default.AtMostALook + 1), (look.Began, look.OutOfReach));
    }

    [Fact(DisplayName = "a recording that failed is not read, its file or no")]
    public async Task ARecordingThatFailedIsNotRead()
    {
        await harness.LearningAsync(true);
        Recording failed = harness.Ended(40, 7631, failed: true);

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((null, 0), (look.Began, look.OutOfReach));
        Assert.Null(harness.Records.Row(failed.Id));
    }

    [Fact(DisplayName = "a record done or partway before them is given whether captions are shown, though more whose captions are not kept than a look takes come first")]
    public async Task CaptionsNotKeptDoNotHoldBackTheRest()
    {
        await harness.LearningAsync(true);
        Recording older = harness.Ended(600, 7632);
        older.Caption(CaptionState.Ready, 2, Now.AddMinutes(-30));
        harness.Records.Hold(Read(older, TimeSpan.FromSeconds(60)));
        harness.Captions.Kept[older.Id] = new CaptionRecord(1440, 1080, TimeSpan.Zero, [Cue(2, shown: true)]);

        for (int lost = 0; lost <= LearningBacklogSettings.Default.AtMostALook; lost++)
        {
            Recording newer = harness.Ended(500 - lost, 7800 + lost);
            newer.Caption(CaptionState.Ready, 2, Now.AddMinutes(-30));
            harness.Records.Hold(Read(newer, TimeSpan.FromSeconds(60)));
        }

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(1, look.Captioned);
        Assert.Contains(harness.Data.Of(older.Id), block => block.Kind is LearningDataKind.CaptionPresence);
    }

    [Fact(DisplayName = "a record waiting, failed with a try left, done another way or left partway before the end is read again from the head")]
    public async Task ARecordThatWaitsIsReadAgain()
    {
        await harness.LearningAsync(true);
        Recording partway = harness.Ended(40, 7620);
        harness.Records.Hold(LeftPartwayBeforeTheEnd(partway));
        LearningBacklogJob job = harness.Job();

        LearningBacklogLook look = await job.LookAsync(Cancel);

        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(partway.Id));
        Assert.Equal(partway.Id, look.Began);
        Assert.Equal((LearningExtractionState.Reading, TimeSpan.Zero, 0), (record.State, record.ReadThrough, record.Gaps.Count));
    }

    [Fact(DisplayName = "a record done the way it is done now, or out of tries, is not read again")]
    public async Task ARecordThatDoesNotWaitIsNotReadAgain()
    {
        await harness.LearningAsync(true);
        Recording done = harness.Ended(40, 7621);
        harness.Records.Hold(Left(done, LearningExtractionState.Done));
        Recording spent = harness.Ended(30, 7622);
        harness.Records.Hold(Spent(spent));

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Null(look.Began);
        Assert.Equal(LearningExtractionState.Done, harness.Records.Row(done.Id)?.State);
        Assert.Equal(LearningExtractionState.Failed, harness.Records.Row(spent.Id)?.State);
    }

    [Fact(DisplayName = "at the first look a record left reading waits to be read, whether or not learning is on")]
    public async Task AtTheFirstLookARecordLeftReadingWaits()
    {
        Recording recording = harness.Ended(40, 7623);
        harness.Records.Hold(Left(recording, LearningExtractionState.Reading));

        LearningBacklogLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal((1, SpareTimeVerdict.LearningOff), (look.Recovered, look.Verdict));
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(recording.Id)?.State);
    }

    [Fact(DisplayName = "a reading that ends leaving its record reading is taken as failed, so that it is tried again a few times only")]
    public async Task AReadingThatLeavesItsRecordReadingIsTakenAsFailed()
    {
        await harness.LearningAsync(true);
        harness.Reader.LeavesItsRecord = true;
        Recording recording = harness.Ended(40, 7624);
        LearningBacklogJob job = harness.Job();
        await job.LookAsync(Cancel);

        harness.Reader.Release(recording.Id);
        LearningExtraction? record = await Eventually.Yields(
            async () =>
            {
                await job.LookAsync(Cancel);

                return harness.Records.Row(recording.Id);
            },
            found => found is { State: LearningExtractionState.Failed, CanRetry: false },
            found => $"{found?.State} after {found?.Failures} failure(s)",
            "the reading was never given up");

        Assert.Equal(LearningExtraction.MostRetries + 1, record?.Failures);
        Assert.Equal(LearningBacklogJob.LeftUnsettled, record?.Failure?.Reason);
        Assert.Equal(LearningExtraction.MostRetries + 1, harness.Reader.Asked.Count);
    }

    [Fact(DisplayName = "in spare time a recording read whose captions are ready is given whether captions are shown, second by second, chunk by chunk")]
    public async Task ARecordingWhoseCaptionsAreReadyIsGivenWhetherCaptionsAreShown()
    {
        await harness.LearningAsync(true);
        Recording captioned = harness.Ended(40, 7625);
        captioned.Caption(CaptionState.Ready, 2, Now.AddMinutes(-30));
        harness.Records.Hold(Read(captioned, TimeSpan.FromSeconds(1300)));
        harness.Captions.Kept[captioned.Id] = new CaptionRecord(1440, 1080, TimeSpan.FromSeconds(10), [Cue(15.5, shown: true), Cue(17, shown: false)]);
        Recording without = harness.Ended(30, 7626);
        without.Caption(CaptionState.Absent, null, Now.AddMinutes(-20));
        harness.Records.Hold(Read(without, TimeSpan.FromSeconds(1300)));

        LearningBacklogJob job = harness.Job();
        LearningBacklogLook look = await job.LookAsync(Cancel);

        LearningDataBlock[] shown = [.. harness.Data.Of(captioned.Id).Where(block => block.Kind is LearningDataKind.CaptionPresence)];
        Assert.Equal(1, look.Captioned);
        Assert.Equal([0, 1, 2], shown.Select(block => block.Chunk));
        Assert.True(shown[0].TryRead(out LearningDataPart? first));
        Assert.Equal([CaptionPresence.Hidden, CaptionPresence.Shown, CaptionPresence.Shown, CaptionPresence.Hidden], first.Captions[4..8].ToArray());
        Assert.Equal(100, shown[2].TryRead(out LearningDataPart? last) ? last.Count : -1);
        Assert.Empty(harness.Data.Of(without.Id));
        Assert.Equal(0, (await job.LookAsync(Cancel)).Captioned);
    }

    [Fact(DisplayName = "whether captions are shown is not written outside spare time, nor for captions not kept")]
    public async Task WhetherCaptionsAreShownIsNotWrittenOutsideSpareTime()
    {
        Recording captioned = harness.Ended(40, 7627);
        captioned.Caption(CaptionState.Ready, 2, Now.AddMinutes(-30));
        harness.Records.Hold(Read(captioned, TimeSpan.FromSeconds(60)));
        LearningBacklogJob job = harness.Job();

        Assert.Equal(0, (await job.LookAsync(Cancel)).Captioned);

        await harness.LearningAsync(true);

        Assert.Equal(0, (await job.LookAsync(Cancel)).Captioned);
        Assert.Empty(harness.Data.Of(captioned.Id));
    }

    private static async Task<bool> RefusedAsync(LearningBacklogJob job)
    {
        try
        {
            await job.LookAsync(Cancel);

            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static async Task<LearningBacklogLook> LetGoAsync(LearningBacklogJob job, Recording read)
        => await Eventually.Yields(
            async () => await job.LookAsync(Cancel),
            look => !read.Id.Equals(look.Reading),
            look => $"still reading {look.Reading?.Wire}",
            "the reading was never let go");

    private async Task<SpareTimeVerdict> OccupyAsync(string occupied)
    {
        switch (occupied)
        {
            case "recording":
                harness.BeingRecorded(7690);

                return SpareTimeVerdict.Recording;
            case "watching":
                harness.Watching.Anyone = true;

                return SpareTimeVerdict.Watching;
            case "a reservation soon":
                await harness.Reservations.AddAsync(ReservationFixtures.Planned(startAt: Now.AddMinutes(45), marginBefore: Margin.OfSeconds(20 * 60)), Cancel);

                return SpareTimeVerdict.ReservationSoon;
            default:
                return SpareTimeVerdict.LearningOff;
        }
    }

    private static LearningExtraction LeftPartwayBeforeTheEnd(Recording recording)
        => LearningExtraction.Rehydrate(
            recording.Id,
            LearningExtractionState.Partial,
            ExtractionVersion.Current,
            TimeSpan.FromSeconds(300),
            [new LearningDataGap(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(30))],
            null,
            null,
            0,
            ProgrammeCopy.Of(recording, null),
            recording.StartedAtActual,
            recording.StartedAtActual.AddSeconds(30));

    private static LearningExtraction Spent(Recording recording)
        => LearningExtraction.Rehydrate(
            recording.Id,
            LearningExtractionState.Failed,
            ExtractionVersion.Current,
            TimeSpan.Zero,
            [],
            null,
            new ExtractionFailureDetail(ExtractionFailure.Other, "it stopped"),
            LearningExtraction.MostRetries + 1,
            ProgrammeCopy.Of(recording, null),
            Now.AddMinutes(-5),
            Now.AddMinutes(-1));

    private static LearningExtraction Read(Recording recording, TimeSpan through)
        => LearningExtraction.Rehydrate(
            recording.Id,
            LearningExtractionState.Done,
            ExtractionVersion.Current,
            through,
            [],
            null,
            null,
            0,
            ProgrammeCopy.Of(recording, null),
            Now.AddMinutes(-5),
            Now.AddMinutes(-1));

    private static CaptionCue Cue(double seconds, bool shown)
        => new((long)(seconds * CaptionCue.Hertz), shown ? new CaptionPlacement(0, 900, 100, 40, new byte[] { 0x89, 1 }) : null);

    private static LearningDataBlock Written(Recording recording)
        => LearningDataBlock.Rehydrate(recording.Id, LearningDataKind.Loudness, 0, [1, 2, 3], ExtractionVersion.Current, Now);
}
