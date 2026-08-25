using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDriverShapeTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static void Take(Recording recording, SessionCounters counters)
    {
        DropPositionsDto? sent = counters.Positions;

        recording.Measure(
            counters.CcMeasured
                ? DropCounters.Counted(counters.Drops, counters.Packets)
                : DropCounters.Unmeasured,
            sent is null
                ? DropTimeline.Unlocated
                : DropTimeline.Rehydrate(
                    sent.AnchorPcr,
                    [
                        .. sent.Buckets.Select(bucket =>
                            new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled)),
                    ],
                    [
                        .. sent.Reanchors.Select(reanchor =>
                            new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After)),
                    ]),
            counters.ScrambleMeasured ? counters.ScrambledPackets : null,
            counters.DeviceOverflows,
            Now);
    }

    [Fact]
    public void ACountWithNothingToPlaceIsTakenAsItStands()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(900_000, [], [])));

        Assert.True(recording.CcMeasured);
        Assert.Equal(0, recording.CcDroppedPackets);
        Assert.True(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void ACountWhoseSecondsAddUpToItIsTakenAsItStands()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                Drops: 9,
                ScrambledPackets: 2,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(
                    900_000,
                    [new DropBucketDto(0, 7, 0), new DropBucketDto(11, 2, 2)],
                    [new PcrReanchorDto(4, 123, 456)])));

        Assert.Equal(9, recording.CcDroppedPackets);
        Assert.Equal(9, recording.Positions.Continuity);
        Assert.Equal(2, recording.Positions.Scrambled);
        Assert.Equal([0, 11], recording.Positions.Buckets.Select(bucket => bucket.Second));
        Assert.Single(recording.Positions.Reanchors);
    }

    [Fact]
    public void ACountThatWasReadWhileItWasStillMovingIsRefusedRatherThanStored()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                Drops: 7,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(900_000, [new DropBucketDto(4, 9, 0)], []))));

        Assert.Equal("positions", refusal.ParamName);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void APositionOnACountNobodyTookIsRefusedRatherThanStored()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Assert.Throws<ArgumentException>(() => Take(
            recording,
            new SessionCounters(Positions: new DropPositionsDto(900_000, [], []))));

        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void ACountFromADriverThatNeverSawTheClockKeepsItsNumbersAndNoPosition()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(Packets: 50_000, Drops: 7, CcMeasured: true));

        Assert.True(recording.CcMeasured);
        Assert.Equal(7, recording.CcDroppedPackets);
        Assert.False(recording.Positions.Located);
    }
}
