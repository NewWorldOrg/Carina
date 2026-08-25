using Carina.Contracts;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDriverShapeTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DriverHello Driver =
        new(
            DriverProtocol.Version,
            "measuring",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.DropPositions,
            ]);

    private static void Take(Recording recording, SessionCounters counters)
    {
        RecordingSessionDto sent = RecordingSessionDto.Of(
            Driver,
            new SessionSnapshot(
                SessionId.Parse("rec-1"),
                SessionPurpose.Recording,
                "adapter0",
                SessionState.Active,
                Now)
            {
                Counters = counters,
            });

        recording.Measure(
            sent.CcMeasured
                ? DropCounters.Counted(sent.CcDropped ?? 0, sent.CcTotal ?? 0)
                : DropCounters.Unmeasured,
            sent.Positions is not { } positions
                ? DropTimeline.Unlocated
                : DropTimeline.Rehydrate(
                    positions.AnchorPcr,
                    [
                        .. positions.Buckets.Select(bucket =>
                            new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled)),
                    ],
                    [
                        .. positions.Reanchors.Select(reanchor =>
                            new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After)),
                    ]),
            sent.ScrambledPackets,
            sent.EovfCount,
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
    public void APositionOnACountNobodyTookNeverReachesTheLedgerAtAll()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(recording, new SessionCounters(Positions: new DropPositionsDto(900_000, [], [])));

        Assert.False(recording.CcMeasured);
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


    [Theory]
    [InlineData(40, 117)]
    [InlineData(20, 304)]
    [InlineData(1, 1_000_000)]
    public void ARecordingThatLostMoreThanItReadIsStillATallyTheLedgerTakes(long read, long lost)
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: read,
                Drops: lost,
                CcMeasured: true,
                ScrambleMeasured: true));

        Assert.Equal(lost, recording.CcDroppedPackets);
        Assert.Equal(read + lost, recording.CcTotalPackets);
    }

    [Fact]
    public void TheTotalIsWhatTheStreamShouldHaveCarriedRatherThanWhatArrived()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(Packets: 900, Drops: 100, CcMeasured: true, ScrambleMeasured: true));

        Assert.Equal(1000, recording.CcTotalPackets);
        Assert.Equal(100, recording.CcDroppedPackets);
    }
}
