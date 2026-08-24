using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingTunerTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Theory]
    [InlineData("pt3-0")]
    [InlineData("px4_0.1")]
    public void ATunerDeviceIdIsANameTheDriverDetected(string name)
        => Assert.Equal(name, new TunerDeviceId(name).Value);

    [Theory]
    [InlineData("/dev/dvb/adapter0")]
    [InlineData("pt3 0")]
    [InlineData("")]
    public void ATunerDeviceIdIsNeverAPath(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new TunerDeviceId(name));

    [Fact]
    public void ATunerDeviceIdIsShortEnoughToCrossTheWire()
        => Assert.Throws<ArgumentException>(
            () => new TunerDeviceId(new string('a', TunerDeviceId.MaxLength + 1)));

    [Fact]
    public void ARecordingNamesTheTunerItWasWrittenBy()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("pt3-1"));

        Assert.Equal(new TunerDeviceId("pt3-1"), recording.TunerDeviceId);
    }

    [Fact]
    public void ARecordingThatHasNotAcquiredATunerYetNamesNone()
    {
        Recording recording = RecordingFactory.Unclaimed();

        Assert.Null(recording.TunerDeviceId);
    }

    [Fact]
    public void ARecordingTakesTheTunerItWasGivenWhenTheSessionOpens()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Acquire(new TunerDeviceId("pt3-2"));

        Assert.Equal(new TunerDeviceId("pt3-2"), recording.TunerDeviceId);
    }

    [Fact]
    public void ACountThatCameOffNoTunerIsRefused()
    {
        Recording recording = RecordingFactory.Unclaimed();

        ArgumentException counted = Assert.Throws<ArgumentException>(
            () => recording.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, null, 0, Now));
        Assert.Equal("tunerDeviceId", counted.ParamName);

        ArgumentException overflowed = Assert.Throws<ArgumentException>(
            () => recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 3, Now));
        Assert.Equal("tunerDeviceId", overflowed.ParamName);
    }

    [Fact]
    public void ARecordingWithNoTunerMayStillSayItCountedNothing()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Now);

        Assert.False(recording.Counters.Measured);
        Assert.Equal(0, recording.EovfCount);
    }

    [Fact]
    public void ARehydratedCountThatCameOffNoTunerIsRefused()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now.AddMinutes(-5),
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Counted(0, 1000),
            DropTimeline.Unlocated,
            null,
            0,
            Now,
            null,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
    }

    [Theory]
    [InlineData(RecordingFault.TuneFailed)]
    [InlineData(RecordingFault.DriverLost)]
    [InlineData(RecordingFault.TunerContended)]
    [InlineData(RecordingFault.ScramblingUnresolved)]
    public void AReasonThatReachedATunerNamesWhichTunerItReached(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Unclaimed();

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Note(new OutcomeDetail(fault, null, string.Empty, Now)));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
        Assert.Empty(recording.OutcomeDetail);
    }

    [Theory]
    [InlineData(RecordingFault.RefusedByDiskPrecheck)]
    [InlineData(RecordingFault.DiskExhausted)]
    [InlineData(RecordingFault.StoppedByHand)]
    [InlineData(RecordingFault.DrainGraceExpired)]
    [InlineData(RecordingFault.ShortOfTheWindow)]
    public void AReasonThatNeverReachedATunerNeedNotNameOne(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Note(new OutcomeDetail(fault, null, string.Empty, Now));

        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void AReasonThatReachedATunerIsTakenOnceTheTunerIsKnown()
    {
        Recording recording = RecordingFactory.Unclaimed();
        recording.Acquire(new TunerDeviceId("pt3-2"));

        recording.Note(new OutcomeDetail(RecordingFault.TuneFailed, TuneFailureKind.NoLock, string.Empty, Now));

        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void ARehydratedReasonThatReachedATunerNamesWhichOne()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            0,
            Now.AddHours(1),
            Now,
            Now.AddHours(1),
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            RecordingOutcome.Failed,
            [new OutcomeDetail(RecordingFault.TunerContended, null, string.Empty, Now)],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Skipped,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
    }

    [Fact]
    public void ASettledRecordingKeepsTheTunerItWasWrittenBy()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("pt3-3"));
        recording.Abort(Now.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1));

        Assert.Equal(new TunerDeviceId("pt3-3"), recording.TunerDeviceId);
        Assert.Throws<InvalidOperationException>(() => recording.Acquire(new TunerDeviceId("pt3-4")));
    }
}
