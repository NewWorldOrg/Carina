using Carina.Broadcast.Sections;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Sections;

public sealed class TransportPacketTests
{
    private const int Pid = 0x0011;

    [Fact]
    public void APacketCarryingASectionStartNamesItsPidAndItsPointer()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, [0x42, 0xF0]);

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.Equal(Pid, packet.Pid);
        Assert.True(packet.PayloadUnitStart);
        Assert.True(packet.HasPayload);
        Assert.False(packet.HasAdaptationField);
        Assert.False(packet.TransportError);
        Assert.False(packet.IsScrambled);
        Assert.Equal(0, packet.ContinuityCounter);
        Assert.Equal(TransportStreamWriter.PayloadCapacity, packet.Payload.Length);
        Assert.Equal(0x00, packet.Payload[0]);
        Assert.Equal(0x42, packet.Payload[1]);
    }

    [Fact]
    public void TheContinuityCounterAdvancesOnEveryPacketThatCarriesPayload()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid)
            .Packet(0, [0x01])
            .Packet(null, [0x02])
            .Packet(null, [0x03]);

        int[] counters = writer.Packets
            .Select(bytes => TransportPacket.TryRead(bytes, out TransportPacket packet) ? packet.ContinuityCounter : -1)
            .ToArray();

        Assert.Equal([0, 1, 2], counters);
    }

    [Fact]
    public void AnAdjustmentFieldShortensThePayloadWithoutMovingIt()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, [0x42, 0xF0], adaptationFieldLength: 20);

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.True(packet.HasAdaptationField);
        Assert.True(packet.HasPayload);
        Assert.Equal(TransportStreamWriter.PayloadCapacity - 21, packet.Payload.Length);
        Assert.Equal(0x00, packet.Payload[0]);
        Assert.Equal(0x42, packet.Payload[1]);
    }

    [Fact]
    public void APacketThatIsAllAdjustmentFieldCarriesNoPayload()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).AdaptationOnlyPacket();

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.True(packet.HasAdaptationField);
        Assert.False(packet.HasPayload);
        Assert.True(packet.Payload.IsEmpty);
    }

    [Fact]
    public void AnAdjustmentFieldLongerThanThePacketIsNotAPacket()
    {
        byte[] bytes = new TransportStreamWriter(Pid).Packet(0, [0x42], adaptationFieldLength: 20).Packets[0];
        bytes[4] = 200;

        Assert.False(TransportPacket.TryRead(bytes, out _));
    }

    [Fact]
    public void AByteRunWithoutTheSyncByteIsNotAPacket()
    {
        byte[] bytes = new TransportStreamWriter(Pid).Packet(0, [0x42]).Packets[0];
        bytes[0] = 0x48;

        Assert.False(TransportPacket.TryRead(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(187)]
    [InlineData(189)]
    public void AByteRunOfTheWrongLengthIsNotAPacket(int length)
    {
        Assert.False(TransportPacket.TryRead(new byte[length], out _));
    }

    [Fact]
    public void ThePacketReportsTheErrorFlagTheDemodulatorRaised()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, [0x42], transportError: true);

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.True(packet.TransportError);
    }

    [Fact]
    public void ThePacketReportsScramblingSoNoOneParsesCipher()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, [0x42], scramblingControl: 0b10);

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.True(packet.IsScrambled);
    }

    [Fact]
    public void TheReservedAdaptationCodeCarriesNothing()
    {
        byte[] bytes = new TransportStreamWriter(Pid).Packet(0, [0x42]).Packets[0];
        bytes[3] = (byte)(bytes[3] & 0b1100_1111);

        Assert.True(TransportPacket.TryRead(bytes, out TransportPacket packet));
        Assert.False(packet.HasPayload);
        Assert.False(packet.HasAdaptationField);
    }

    [Fact]
    public void TheNullPacketPidIsTheHighestThirteenBitValue()
    {
        TransportStreamWriter writer = new TransportStreamWriter(TransportPacket.NullPacketPid).Packet(null, [0x00]);

        Assert.True(TransportPacket.TryRead(writer.Packets[0], out TransportPacket packet));
        Assert.Equal(0x1FFF, packet.Pid);
    }
}
