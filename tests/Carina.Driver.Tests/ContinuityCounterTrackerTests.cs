using Carina.Contracts;
using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class ContinuityCounterTrackerTests
{
    private static TsPacket Packet(
        int pid,
        int continuityCounter,
        bool hasPayload = true,
        int payloadHash = 1,
        bool transportError = false,
        bool scrambled = false,
        bool discontinuity = false,
        long? pcr = null
    ) =>
        new(
            pid,
            continuityCounter,
            hasPayload,
            transportError,
            scrambled,
            discontinuity,
            PayloadUnitStart: false,
            payloadHash,
            Pcr: pcr
        );

    [Fact]
    public void AnUninterruptedStreamLosesNothing()
    {
        var tracker = new ContinuityCounterTracker();

        for (int counter = 0; counter < 32; counter++)
        {
            tracker.Observe(Packet(0x100, counter % 16));
        }

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(32, tracker.Packets);
    }

    [Fact]
    public void TheCounterWrappingIsNotALoss()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 15));
        tracker.Observe(Packet(0x100, 0));

        Assert.Equal(0, tracker.Drops);
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(0, 5, 4)]
    [InlineData(14, 1, 2)]
    public void AGapCountsThePacketsBetween(int before, int after, int expected)
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, before));
        tracker.Observe(Packet(0x100, after));

        Assert.Equal(expected, tracker.Drops);
    }

    [Fact]
    public void EachStreamIsCountedSeparately()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(0x200, 7));
        tracker.Observe(Packet(0x100, 1));
        tracker.Observe(Packet(0x200, 8));

        Assert.Equal(0, tracker.Drops);
    }

    [Fact]
    public void ALossIsAttributedToItsOwnStream()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(0x100, 3));
        tracker.Observe(Packet(0x200, 0));
        tracker.Observe(Packet(0x200, 1));

        Assert.Equal(2, tracker.Drops);
        Assert.Equal(2, tracker.DropsFor(0x100));
        Assert.Equal(0, tracker.DropsFor(0x200));
    }

    [Fact]
    public void PaddingIsNotMeasured()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(TsPacket.NullPid, 0));
        tracker.Observe(Packet(TsPacket.NullPid, 9));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(0, tracker.Packets);
    }

    [Fact]
    public void ARepeatedCounterWithoutPayloadIsNotALoss()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x100, 4, hasPayload: false));

        Assert.Equal(0, tracker.Drops);
    }

    [Fact]
    public void ARepeatedPacketIsNotCountedAsFifteenLosses()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, payloadHash: 99));
        tracker.Observe(Packet(0x100, 4, payloadHash: 99));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(1, tracker.Duplicates);
    }

    [Fact]
    public void ACounterThatCameBackAroundIsSixteenLossesAndNotARepeat()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, payloadHash: 99));
        tracker.Observe(Packet(0x100, 4, payloadHash: 12345));

        Assert.Equal(16, tracker.Drops);
        Assert.Equal(0, tracker.Duplicates);
    }

    [Fact]
    public void AMarkedDiscontinuityIsNotALoss()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x100, 9, discontinuity: true));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(1, tracker.Discontinuities);
    }

    [Fact]
    public void APacketTheHardwareFlaggedIsNotMeasured()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x100, 12, transportError: true));
        tracker.Observe(Packet(0x100, 5));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(1, tracker.TransportErrors);
        Assert.Equal(2, tracker.Packets);
    }

    [Fact]
    public void StillScrambledPacketsAreCountedInTheSamePass()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, scrambled: true));
        tracker.Observe(Packet(0x100, 1));

        Assert.Equal(1, tracker.ScrambledPackets);
        Assert.Equal(2, tracker.Packets);
    }

    [Fact]
    public void ARetuneDoesNotCarryTheOldCountersIntoTheNewStream()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 2));
        tracker.Retuned();
        tracker.Observe(Packet(0x100, 11));

        Assert.Equal(0, tracker.Drops);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0x2000, 0)]
    [InlineData(0x100, 16)]
    [InlineData(0x100, -1)]
    public void APacketOutsideTheHeaderRangesIsIgnored(int pid, int continuityCounter)
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(pid, continuityCounter));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(1, tracker.Packets);
    }

    [Fact]
    public void APacketFromAnUnprovenBoundaryIsNotMeasured()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(0x100, 9) with { Provisional = true });
        tracker.Observe(Packet(0x100, 1));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(2, tracker.Packets);
        Assert.Equal(1, tracker.ProvisionalPackets);
    }

    [Fact]
    public void TheFirstPacketOfAStreamIsNotALoss()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 9));

        Assert.Equal(0, tracker.Drops);
    }

    [Fact]
    public void AStreamNobodyHasReadYetHasCountedNothingRatherThanCountedZero()
    {
        var tracker = new ContinuityCounterTracker();

        Assert.False(tracker.CcMeasured);
        Assert.False(tracker.ScrambleMeasured);
        Assert.Null(tracker.Snapshot().Positions);
    }

    [Fact]
    public void AStreamThatWasReadAndLostNothingHasCountedZeroRatherThanCountedNothing()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 4_500_000));
        tracker.Observe(Packet(0x100, 1));

        Assert.True(tracker.CcMeasured);
        Assert.True(tracker.ScrambleMeasured);
        Assert.Equal(0, tracker.Drops);
        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Empty(positions.Buckets);
        Assert.Equal(4_500_000, positions.AnchorPcr);
    }

    [Fact]
    public void PaddingAloneIsNotAMeasurement()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(TsPacket.NullPid, 0));
        tracker.Observe(Packet(TsPacket.NullPid, 5));

        Assert.False(tracker.CcMeasured);
        Assert.False(tracker.ScrambleMeasured);
    }

    [Fact]
    public void PacketsFromAnUnprovenBoundaryAloneAreNotAMeasurement()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0) with { Provisional = true });
        tracker.Observe(Packet(0x100, 5) with { Provisional = true });

        Assert.False(tracker.CcMeasured);
        Assert.False(tracker.ScrambleMeasured);
    }

    [Fact]
    public void AStreamThatNeverSaysWhatTimeItIsIsCountedWithoutBeingPlaced()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(0x100, 4));

        Assert.True(tracker.CcMeasured);
        Assert.Equal(3, tracker.Drops);
        Assert.Null(tracker.Snapshot().Positions);
    }

    [Fact]
    public void ALossIsWrittenAgainstTheSecondTheClockWasReadingWhenItHappened()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 1, pcr: 4 * 90_000));
        tracker.Observe(Packet(0x100, 4));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal([new DropBucketDto(4, 2, 0)], positions.Buckets);
    }

    [Fact]
    public void APacketThatBothSaysTheTimeAndFinishesAGapIsPlacedAtTheTimeItSays()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 4, pcr: 6 * 90_000));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal([new DropBucketDto(6, 3, 0)], positions.Buckets);
    }

    [Fact]
    public void ALossBeforeTheClockWasEverReadIsPlacedAtTheStart()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0));
        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x100, 5, pcr: 4_500_000));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal([new DropBucketDto(0, 3, 0)], positions.Buckets);
    }

    [Fact]
    public void OnlyTheSecondsWhereSomethingHappenedAreNamed()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 1, pcr: 5 * 90_000));
        tracker.Observe(Packet(0x100, 3));
        tracker.Observe(Packet(0x100, 4, pcr: 9 * 90_000));
        tracker.Observe(Packet(0x100, 5, pcr: 12 * 90_000));
        tracker.Observe(Packet(0x100, 9));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal([5, 12], positions.Buckets.Select(bucket => bucket.Second));
        Assert.Equal([1, 3], positions.Buckets.Select(bucket => bucket.Continuity));
    }

    [Fact]
    public void EverythingPlacedAddsUpToWhatWasCounted()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 4, pcr: 3 * 90_000));
        tracker.Observe(Packet(0x100, 5, scrambled: true));
        tracker.Observe(Packet(0x100, 9, pcr: 8 * 90_000, scrambled: true));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal(
            tracker.Drops,
            positions.Buckets.Sum(bucket => bucket.Continuity)
        );
        Assert.Equal(
            tracker.ScrambledPackets,
            positions.Buckets.Sum(bucket => bucket.Scrambled)
        );
    }

    [Fact]
    public void PacketsLeftScrambledArePlacedApartFromPacketsThatWentMissing()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 1, pcr: 6 * 90_000, scrambled: true));
        tracker.Observe(Packet(0x100, 4, pcr: 9 * 90_000));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal(
            [new DropBucketDto(6, 0, 1), new DropBucketDto(9, 2, 0)],
            positions.Buckets
        );
    }

    [Fact]
    public void SeveralLossesInOneSecondAreOneEntry()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 0));
        tracker.Observe(Packet(0x100, 1, pcr: 7 * 90_000));
        tracker.Observe(Packet(0x200, 0));
        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x200, 5));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal([new DropBucketDto(7, 6, 0)], positions.Buckets);
    }

    [Fact]
    public void ARepeatedPacketPlacesNothing()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, payloadHash: 99, pcr: 3 * 90_000));
        tracker.Observe(Packet(0x100, 4, payloadHash: 99));

        Assert.Equal(1, tracker.Duplicates);
        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Empty(positions.Buckets);
    }

    [Fact]
    public void AMarkedDiscontinuityPlacesNothing()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, pcr: 3 * 90_000));
        tracker.Observe(Packet(0x100, 9, discontinuity: true));

        Assert.Equal(1, tracker.Discontinuities);
        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Empty(positions.Buckets);
    }

    [Fact]
    public void PaddingPlacesNothing()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, pcr: 3 * 90_000));
        tracker.Observe(Packet(TsPacket.NullPid, 0));
        tracker.Observe(Packet(TsPacket.NullPid, 9));
        tracker.Observe(Packet(0x100, 5));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Empty(positions.Buckets);
    }

    [Fact]
    public void APacketTheHardwareFlaggedPlacesNothingAndMovesNoClock()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 4, pcr: 3 * 90_000));
        tracker.Observe(Packet(0x100, 12, transportError: true, pcr: 900 * 90_000));
        tracker.Observe(Packet(0x100, 5));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Empty(positions.Buckets);
        Assert.Empty(positions.Reanchors);
    }

    [Fact]
    public void AClockThatBreaksIsCarriedBesideTheSecondsItPlaced()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 100 * 90_000));
        tracker.Observe(Packet(0x100, 1, pcr: 104 * 90_000));
        tracker.Observe(Packet(0x100, 2, pcr: 20 * 90_000));
        tracker.Observe(Packet(0x100, 6));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal(100 * 90_000, positions.AnchorPcr);
        Assert.Equal(
            [new PcrReanchorDto(4, 104 * 90_000, 20 * 90_000)],
            positions.Reanchors
        );
        Assert.Equal([new DropBucketDto(4, 3, 0)], positions.Buckets);
    }

    [Fact]
    public void TheSecondsAPositionNamesOnlyEverReadForwards()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 0, pcr: 100 * 90_000));
        tracker.Observe(Packet(0x100, 4, pcr: 110 * 90_000));
        tracker.Observe(Packet(0x100, 5, pcr: 30 * 90_000));
        tracker.Observe(Packet(0x100, 9, pcr: 34 * 90_000));

        DropPositionsDto? positions = tracker.Snapshot().Positions;

        Assert.NotNull(positions);
        Assert.Equal(
            positions.Buckets.Select(bucket => bucket.Second).Order(),
            positions.Buckets.Select(bucket => bucket.Second)
        );
        Assert.Equal(
            [10, 14],
            positions.Buckets.Select(bucket => bucket.Second)
        );
    }
}
