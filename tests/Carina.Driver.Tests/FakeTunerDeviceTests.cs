using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class FakeTunerDeviceTests
{
    private static byte[] Read(FakeTunerDevice device, int packets) =>
        device.Read(packets * TsPacketReader.PacketLength);

    [Fact]
    public void ProducesPacketsAReaderCanRead()
    {
        var device = new FakeTunerDevice(physicalChannel: 27, serviceId: 1024);
        var reader = new TsPacketReader();

        var packets = reader.Read(Read(device, 10));

        Assert.Equal(10, packets.Count);
        Assert.All(packets, packet => Assert.False(packet.IsNull));
    }

    [Fact]
    public void TheSameRequestProducesTheSameBytes()
    {
        var first = new FakeTunerDevice(27, 1024);
        var second = new FakeTunerDevice(27, 1024);

        Assert.Equal(Read(first, 20), Read(second, 20));
    }

    [Fact]
    public void ADifferentRequestProducesADifferentStream()
    {
        var terrestrial = new FakeTunerDevice(27, 1024);
        var other = new FakeTunerDevice(21, 1024);

        Assert.NotEqual(Read(terrestrial, 20), Read(other, 20));
    }

    [Fact]
    public void ItsContinuityCountersNeverBreak()
    {
        var device = new FakeTunerDevice(27, 1024);
        var reader = new TsPacketReader();
        var tracker = new ContinuityCounterTracker();

        for (var read = 0; read < 5; read++)
        {
            foreach (var packet in reader.Read(Read(device, 100)))
            {
                tracker.Observe(packet);
            }
        }

        Assert.Equal(0, tracker.Drops);
        Assert.Equal(0, tracker.Duplicates);
        Assert.Equal(500, tracker.Packets);
    }

    [Fact]
    public void ReadingContinuesWhereTheLastReadStopped()
    {
        var device = new FakeTunerDevice(27, 1024);
        var reader = new TsPacketReader();

        var first = reader.Read(Read(device, 1));
        var second = reader.Read(Read(device, 1));

        Assert.Equal(0, first[0].ContinuityCounter);
        Assert.Equal(1, second[0].ContinuityCounter);
    }

    [Fact]
    public void ReadsThatStopMidPacketStillLineUp()
    {
        var device = new FakeTunerDevice(27, 1024);
        var reader = new TsPacketReader();

        var packets = reader.Read(device.Read(100)).Concat(reader.Read(device.Read(276))).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal([0, 1], packets.Select(packet => packet.ContinuityCounter));
    }
}
