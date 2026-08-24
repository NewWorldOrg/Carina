using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingPositionTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void ARecordingBeginsWithNoPositionAtAll()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void APositionCannotRideOnAMeasurementNobodyTook()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Unmeasured,
            DropTimeline.AnchoredAt(900_000),
            null,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void ATimelineCannotPlaceMoreLostPacketsThanWereCounted()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Counted(2, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact]
    public void ATimelineCannotPlaceMoreScrambledPacketsThanWereCounted()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Counted(0, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 0, 3)], []),
            1,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact]
    public void AMeasurementWithPositionsKeepsBoth()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now);

        Assert.Equal(3, recording.Counters.Dropped);
        Assert.True(recording.Positions.Located);
        Assert.Equal(12, Assert.Single(recording.Positions.Buckets).Second);
    }

    [Fact]
    public void AMeasurementTakenWithoutPositionsIsStillAMeasurement()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1000), DropTimeline.Unlocated, null, 0, Now);

        Assert.True(recording.Counters.Measured);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void MeasurementThatBreaksMidWayTakesThePositionsWithIt()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now);

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Now.AddMinutes(1));

        Assert.False(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void ARehydratedPositionOnAnUncountedRecordingIsRefused()
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
            DropCounters.Unmeasured,
            DropTimeline.AnchoredAt(900_000),
            null,
            0,
            null,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("positions", refusal.ParamName);
    }
}
