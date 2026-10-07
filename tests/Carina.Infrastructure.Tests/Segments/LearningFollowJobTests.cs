using Carina.Contracts;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.LearningFollowHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningFollowJobTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly LearningFollowHarness harness = new();

    public void Dispose() => harness.Dispose();

    [Fact(DisplayName = "while learning is off a recording being written is not followed, and its record waits with the copy of its programme")]
    public async Task WhileLearningIsOffARecordingWaitsWithTheCopyOfItsProgramme()
    {
        Recording recording = harness.Recording();
        harness.Worklist.Ends[recording.Id] = Now.AddMinutes(55);

        LearningLook look = await harness.Job().LookAsync(Cancel);

        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(recording.Id));
        Assert.Equal(LearningExtractionState.Waiting, record.State);
        Assert.Null(record.Version);
        Assert.Equal(ProgrammeCopy.Of(recording, Now.AddMinutes(55)), record.Programme);
        Assert.Empty(harness.Follower.Asked);
        Assert.Equal(new LearningLook(0, 0, 0, 1, 0), look);
    }

    [Fact(DisplayName = "while learning is on a recording being written is followed from the head of its file, its record following")]
    public async Task WhileLearningIsOnARecordingIsFollowedFromTheHead()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Recording();
        LearningFollowJob job = harness.Job();

        LearningLook look = await job.LookAsync(Cancel);

        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(recording.Id));
        Assert.Equal((LearningExtractionState.Following, ExtractionVersion.Current, TimeSpan.Zero), (record.State, record.Version, record.ReadThrough));
        Assert.Equal([recording.Id], job.Following);
        await Eventually.Happens(() => !harness.Follower.Asked.IsEmpty, "the follow never began");
        FollowedRecording followed = Assert.Single(harness.Follower.Asked);
        Assert.Equal((recording.Id, harness.Source(recording), recording.ServiceId), (followed.Id, followed.Source, followed.Service));
        Assert.Equal(new LearningLook(0, 1, 0, 0, 1), look);

        Assert.Equal(0, (await job.LookAsync(Cancel)).Began);
        Assert.Single(harness.Follower.Asked);
    }

    [Fact(DisplayName = "a recording whose record waits is followed from its head once learning is on")]
    public async Task AWaitingRecordIsFollowedOnceLearningIsOn()
    {
        Recording recording = harness.Recording();
        LearningFollowJob job = harness.Job();
        await job.LookAsync(Cancel);

        await harness.LearningAsync(true);
        await job.LookAsync(Cancel);

        Assert.Equal(LearningExtractionState.Following, harness.Records.Row(recording.Id)?.State);
        Assert.Equal([recording.Id], job.Following);
    }

    [Fact(DisplayName = "switching learning off stops the follows, keeps what they wrote, and puts their records back to wait")]
    public async Task SwitchingLearningOffStopsTheFollowsAndPutsTheirRecordsBackToWait()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Recording();
        LearningFollowJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => !harness.Follower.Asked.IsEmpty, "the follow never began");
        await harness.Data.KeepAsync(Written(recording), Cancel);

        await harness.LearningAsync(false);
        LearningLook look = await job.LookAsync(Cancel);

        Assert.Equal([recording.Id], harness.Follower.Stopped);
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(recording.Id)?.State);
        Assert.Single(harness.Data.Of(recording.Id));
        Assert.Empty(job.Following);
        Assert.Equal(new LearningLook(0, 0, 1, 0, 0), look);
    }

    [Fact(DisplayName = "at the first look a record left following a recording still being written is followed again from the head")]
    public async Task AtTheFirstLookARecordLeftFollowingIsFollowedAgainFromTheHead()
    {
        await harness.LearningAsync(true);
        Recording recording = harness.Recording();
        harness.Records.Hold(Left(recording, LearningExtractionState.Following));
        LearningFollowJob job = harness.Job();

        await job.LookAsync(Cancel);

        LearningExtraction record = Assert.IsType<LearningExtraction>(harness.Records.Row(recording.Id));
        Assert.Equal((LearningExtractionState.Following, TimeSpan.Zero, 0, null), (record.State, record.ReadThrough, record.Gaps.Count, record.Sound));
        Assert.Equal([recording.Id], job.Following);
    }

    [Fact(DisplayName = "at the first look a record left following or reading a recording that ended or went waits to be read")]
    public async Task AtTheFirstLookARecordLeftRunningForAnEndedRecordingWaits()
    {
        await harness.LearningAsync(true);
        Recording ended = harness.Recording(eventId: 7302);
        End(ended);
        Recording gone = harness.Recording(eventId: 7303);
        harness.Worklist.Recordings.Remove(gone);
        harness.Records.Hold(Left(ended, LearningExtractionState.Following));
        harness.Records.Hold(Left(gone, LearningExtractionState.Reading));

        LearningLook look = await harness.Job().LookAsync(Cancel);

        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(ended.Id)?.State);
        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(gone.Id)?.State);
        Assert.Equal(2, look.Recovered);
        Assert.Empty(harness.Follower.Asked);
    }

    [Fact(DisplayName = "at the first look with learning off a record left following a recording still being written waits")]
    public async Task AtTheFirstLookWithLearningOffARecordLeftFollowingWaits()
    {
        Recording recording = harness.Recording();
        harness.Records.Hold(Left(recording, LearningExtractionState.Following));

        await harness.Job().LookAsync(Cancel);

        Assert.Equal(LearningExtractionState.Waiting, harness.Records.Row(recording.Id)?.State);
        Assert.Empty(harness.Follower.Asked);
    }

    [Fact(DisplayName = "a follow is told when its recording ends, and when its recording goes")]
    public async Task AFollowIsToldWhenItsRecordingEndsOrGoes()
    {
        await harness.LearningAsync(true);
        Recording ending = harness.Recording(eventId: 7304);
        Recording going = harness.Recording(eventId: 7305);
        LearningFollowJob job = harness.Job();
        await job.LookAsync(Cancel);
        await Eventually.Happens(() => harness.Follower.Asked.Count is 2, "the follows never began");

        End(ending);
        harness.Worklist.Recordings.Remove(going);
        await job.LookAsync(Cancel);

        FollowedRecording[] asked = [.. harness.Follower.Asked];
        Assert.True(asked.Single(followed => followed.Id.Equals(ending.Id)).HasEnded);
        Assert.True(asked.Single(followed => followed.Id.Equals(going.Id)).HasGone);
        await Eventually.Happens(() => harness.Records.Row(ending.Id)?.State is LearningExtractionState.Done, "the ended follow never settled");
        await Eventually.Yields(
            async () => (await job.LookAsync(Cancel)).Following,
            following => following is 0,
            following => $"{following} follow(s)",
            "the settled follows were never let go");
    }

    [Fact(DisplayName = "a recording under a root this process does not mount is not looked at, and one whose file is not there yet is not followed yet")]
    public async Task ARecordingOutOfReachOrWithoutItsFileIsNotFollowed()
    {
        await harness.LearningAsync(true);
        Recording unreached = harness.Recording(Unmounted, eventId: 7306);
        Recording early = harness.Recording(withAFile: false, eventId: 7307);

        LearningLook look = await harness.Job().LookAsync(Cancel);

        Assert.Null(harness.Records.Row(unreached.Id));
        Assert.Null(harness.Records.Row(early.Id));
        Assert.Equal(0, look.Began);
    }

    [Fact(DisplayName = "the first look comes after its wait, and the driver telling of a recording's progress wakes the next before its wait is over")]
    public async Task TheDriversProgressWakesTheNextLook()
    {
        await harness.LearningAsync(true);
        Recording first = harness.Recording(eventId: 7308);
        using LearningFollowJob job = harness.Job(new LearningFollowSettings
        {
            BeforeFirstLook = TimeSpan.FromMilliseconds(10),
            BetweenLooks = TimeSpan.FromHours(1),
        });
        using CancellationTokenSource stopping = new();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => harness.Records.Row(first.Id) is not null, "the first look never came");

        Recording second = harness.Recording(eventId: 7309);
        harness.Signals.Publish(DriverEvents.RecordingProgress);

        await Eventually.Happens(() => harness.Records.Row(second.Id) is not null, "the progress never woke a look");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal(2, harness.Follower.Stopped.Count);
        Assert.Equal(LearningExtractionState.Following, harness.Records.Row(first.Id)?.State);
    }

    [Fact(DisplayName = "a change to a record that moved after it was read is made again on the record as it stands")]
    public async Task AChangeToARecordThatMovedIsMadeAgain()
    {
        Recording recording = harness.Recording();
        harness.Records.Hold(LearningExtraction.Waiting(recording.Id, ProgrammeCopy.Of(recording, null), Now));
        int moves = 1;
        harness.Records.MovesBeforeSaving = _ => moves-- > 0;

        ExtractionChange change = await harness.Store.ChangeAsync(
            recording.Id,
            record =>
            {
                record.Follow(ExtractionVersion.Current, Now);

                return true;
            },
            Cancel);

        Assert.Equal(ExtractionChange.Written, change);
        Assert.Equal(1, harness.Records.Refused);
        Assert.Equal(LearningExtractionState.Following, harness.Records.Row(recording.Id)?.State);
    }

    [Fact(DisplayName = "a change to a record that keeps moving, or that is not there, writes nothing")]
    public async Task AChangeToARecordThatKeepsMovingWritesNothing()
    {
        Recording recording = harness.Recording();
        harness.Records.Hold(LearningExtraction.Waiting(recording.Id, ProgrammeCopy.Of(recording, null), Now));
        harness.Records.MovesBeforeSaving = _ => true;

        Assert.Equal(ExtractionChange.KeptMoving, await harness.Store.ChangeAsync(recording.Id, _ => true, Cancel));
        Assert.Equal(LearningRecords.Attempts, harness.Records.Refused);
        Assert.Equal(ExtractionChange.Missing, await harness.Store.ChangeAsync(RecordingId.New(), _ => true, Cancel));
        Assert.Equal(ExtractionChange.Declined, await harness.Store.ChangeAsync(recording.Id, _ => false, Cancel));
    }

    private static LearningDataBlock Written(Recording recording)
        => LearningDataBlock.Rehydrate(recording.Id, LearningDataKind.Loudness, 0, [1, 2, 3], ExtractionVersion.Current, Now);
}
