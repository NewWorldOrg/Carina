using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingGapTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void AGapLastsFromTheLastWriteBeforeItToTheFirstWriteAfterIt()
    {
        RecordingGap gap = new(Now.AddMinutes(9).AddSeconds(16.2), Now.AddMinutes(9).AddSeconds(19.7));

        Assert.Equal(TimeSpan.FromSeconds(3.5), gap.Lasts);
    }

    [Fact]
    public void AGapEndsAfterItBegins()
    {
        Assert.Throws<ArgumentException>(() => new RecordingGap(Now, Now));
        Assert.Throws<ArgumentException>(() => new RecordingGap(Now, Now.AddSeconds(-1)));
    }

    [Fact]
    public void AGapIsTimedOnTheSameClockAsTheRestOfTheRecording()
        => Assert.Throws<ArgumentException>(
            () => new RecordingGap(DateTime.SpecifyKind(Now, DateTimeKind.Local), Now.AddSeconds(3)));

    [Fact]
    public void AGapKeptOnARecordingIsAddedToWhatItMissed()
    {
        Recording recording = RecordingFactory.Started();

        recording.Missed(new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(63.5)));
        recording.Missed(new RecordingGap(Now.AddSeconds(300), Now.AddSeconds(301)));

        Assert.Equal(2, recording.Gaps.Count);
        Assert.Equal(4_500, recording.MissedMs);
    }

    [Fact]
    public void TheSameGapSeenAgainIsKeptOnce()
    {
        Recording recording = RecordingFactory.Started();
        RecordingGap gap = new(Now.AddSeconds(60), Now.AddSeconds(63.5));

        recording.Missed(gap);
        recording.Missed(gap);

        Assert.Single(recording.Gaps);
        Assert.Equal(3_500, recording.MissedMs);
    }

    [Fact]
    public void AGapThatWouldFallBeforeTheRecordingBeganOrBeforeTheLastOneEndedIsRefused()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentException>(() => recording.Missed(new RecordingGap(Now.AddSeconds(-1), Now.AddSeconds(2))));

        recording.Missed(new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(63.5)));

        Assert.Throws<ArgumentException>(() => recording.Missed(new RecordingGap(Now.AddSeconds(62), Now.AddSeconds(70))));
        Assert.Single(recording.Gaps);
    }

    [Fact]
    public void AGapShorterThanAMillisecondStillCountsAsSomethingMissed()
    {
        Recording recording = RecordingFactory.Started();

        recording.Missed(new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(60).AddTicks(10)));

        Assert.Equal(1, recording.MissedMs);
    }

    [Fact]
    public void EachGapIsPlacedWhereItFallsInWhatWasWritten()
    {
        IReadOnlyList<TimeSpan> placed = RecordingGap.Placed(
            Now,
            [
                new RecordingGap(Now.AddSeconds(556), Now.AddSeconds(559.5)),
                new RecordingGap(Now.AddSeconds(700), Now.AddSeconds(702)),
            ]);

        Assert.Equal([TimeSpan.FromSeconds(556), TimeSpan.FromSeconds(696.5)], placed);
    }

    [Fact]
    public void ARecordingReadBackWithGapsThatOverlapIsRefused()
        => Assert.Throws<ArgumentException>(() => Rehydrated(
            [
                new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(63.5)),
                new RecordingGap(Now.AddSeconds(63), Now.AddSeconds(64)),
            ]));

    [Fact]
    public void ARecordingReadBackWithGapsMissedWhatTheyAddUpTo()
        => Assert.Equal(
            3_500,
            Rehydrated([new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(63.5))]).MissedMs);

    private static Recording Rehydrated(IReadOnlyList<RecordingGap> gaps)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now,
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            Carina.Domain.Reservations.BroadcastGroupRole.Standalone,
            gaps: gaps);
    }
}
