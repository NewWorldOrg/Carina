using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class ContinuityCounterTrackerTests
{
    private static TsPacket Packet(int pid, int continuityCounter, bool hasPayload = true) =>
        new(pid, continuityCounter, hasPayload);

    [Fact]
    public void AnUninterruptedStreamLosesNothing()
    {
        var tracker = new ContinuityCounterTracker();

        for (var counter = 0; counter < 32; counter++)
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

        tracker.Observe(Packet(0x100, 4));
        tracker.Observe(Packet(0x100, 4));

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(1, tracker.Duplicates);
    }

    [Fact]
    public void TheFirstPacketOfAStreamIsNotALoss()
    {
        var tracker = new ContinuityCounterTracker();

        tracker.Observe(Packet(0x100, 9));

        Assert.Equal(0, tracker.Drops);
    }
}
