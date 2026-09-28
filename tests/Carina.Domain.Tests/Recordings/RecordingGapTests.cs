using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingGapTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void AGapLastsFromTheLastWriteBeforeItToTheFirstWriteAfterIt()
    {
        RecordingGap gap = new(Now.AddMinutes(5).AddSeconds(12), Now.AddMinutes(5).AddSeconds(14.5));

        Assert.Equal(TimeSpan.FromSeconds(2.5), gap.Lasts);
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

        recording.Missed(new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(62.5)));
        recording.Missed(new RecordingGap(Now.AddSeconds(300), Now.AddSeconds(301)));

        Assert.Equal(2, recording.Gaps.Count);
        Assert.Equal(3_500, recording.MissedMs);
    }

    [Fact]
    public void TheSameGapSeenAgainIsKeptOnce()
    {
        Recording recording = RecordingFactory.Started();
        RecordingGap gap = new(Now.AddSeconds(60), Now.AddSeconds(62.5));

        recording.Missed(gap);
        recording.Missed(gap);

        Assert.Single(recording.Gaps);
        Assert.Equal(2_500, recording.MissedMs);
    }

    [Fact]
    public void AGapThatWouldFallBeforeTheRecordingBeganOrBeforeTheLastOneEndedIsRefused()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentException>(() => recording.Missed(new RecordingGap(Now.AddSeconds(-1), Now.AddSeconds(2))));

        recording.Missed(new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(62.5)));

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
    public void EachGapIsPlacedOnTheClockTheFileIsPlayedByWhichKeepsRunningThroughEarlierGaps()
    {
        IReadOnlyList<RecordingSeam> seams = RecordingGap.SeamsIn(
            Now,
            [
                new RecordingGap(Now.AddSeconds(312), Now.AddSeconds(314.5)),
                new RecordingGap(Now.AddSeconds(700), Now.AddSeconds(702)),
            ]);

        Assert.Equal(
            [
                new RecordingSeam(TimeSpan.FromSeconds(312), TimeSpan.FromSeconds(314.5)),
                new RecordingSeam(TimeSpan.FromSeconds(700), TimeSpan.FromSeconds(702)),
            ],
            seams);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(308.9, 308.9)]
    [InlineData(309, 317.5)]
    [InlineData(313, 317.5)]
    [InlineData(317.5, 317.5)]
    [InlineData(317.6, 317.6)]
    public void AStillIsTakenPastASeamItWouldFallNear(double asked, double taken)
        => Assert.Equal(
            TimeSpan.FromSeconds(taken),
            RecordingSeam.KeepClear(
                TimeSpan.FromSeconds(asked),
                [new RecordingSeam(TimeSpan.FromSeconds(312), TimeSpan.FromSeconds(314.5))]));

    [Fact]
    public void AStillMovedPastOneSeamIsMovedPastTheNextWhenItLandsNearThatOneToo()
        => Assert.Equal(
            TimeSpan.FromSeconds(325),
            RecordingSeam.KeepClear(
                TimeSpan.FromSeconds(313),
                [
                    new RecordingSeam(TimeSpan.FromSeconds(312), TimeSpan.FromSeconds(314.5)),
                    new RecordingSeam(TimeSpan.FromSeconds(319), TimeSpan.FromSeconds(322)),
                ]));

    [Fact]
    public void ARecordingReadBackWithGapsThatOverlapIsRefused()
        => Assert.Throws<ArgumentException>(() => Rehydrated(
            [
                new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(62.5)),
                new RecordingGap(Now.AddSeconds(62), Now.AddSeconds(64)),
            ]));

    [Fact]
    public void ARecordingReadBackWithGapsMissedWhatTheyAddUpTo()
        => Assert.Equal(
            2_500,
            Rehydrated([new RecordingGap(Now.AddSeconds(60), Now.AddSeconds(62.5))]).MissedMs);

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
