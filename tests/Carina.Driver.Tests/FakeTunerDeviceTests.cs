using Carina.Driver.Transport;
using Carina.Driver.Tuning;

namespace Carina.Driver.Tests;

public sealed class FakeTunerDeviceTests
{
    private static byte[] Read(FakeTunerDevice device, int packets) =>
        device.Read(packets * TsPacketReader.PacketLength, CancellationToken.None);

    [Fact]
    public void ProducesPacketsAReaderCanRead()
    {
        var device = new FakeTunerDevice(physicalChannel: 200, serviceId: 50001);
        var reader = new TsPacketReader();

        var packets = reader.Read(Read(device, 10));

        Assert.Equal(10, packets.Count);
        Assert.All(packets, packet => Assert.False(packet.IsNull));
    }

    [Fact]
    public void TheSameRequestProducesTheSameBytes()
    {
        var first = new FakeTunerDevice(200, 50001);
        var second = new FakeTunerDevice(200, 50001);

        Assert.Equal(Read(first, 20), Read(second, 20));
    }

    [Fact]
    public void ADifferentRequestProducesADifferentStream()
    {
        var terrestrial = new FakeTunerDevice(200, 50001);
        var other = new FakeTunerDevice(201, 50001);

        Assert.NotEqual(Read(terrestrial, 20), Read(other, 20));
    }

    [Fact]
    public void ItsContinuityCountersNeverBreak()
    {
        var device = new FakeTunerDevice(200, 50001);
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
        var device = new FakeTunerDevice(200, 50001);
        var reader = new TsPacketReader();

        var first = reader.Read(Read(device, 1));
        var second = reader.Read(Read(device, 1));

        Assert.Equal(0, first[0].ContinuityCounter);
        Assert.Equal(1, second[0].ContinuityCounter);
    }

    [Fact]
    public void ReadsThatStopMidPacketStillLineUp()
    {
        var device = new FakeTunerDevice(200, 50001);
        var reader = new TsPacketReader();

        var packets = reader
            .Read(device.Read(100, CancellationToken.None))
            .Concat(reader.Read(device.Read(276, CancellationToken.None)))
            .ToList();

        Assert.Equal([0, 1], packets.Select(packet => packet.ContinuityCounter));
    }
}
