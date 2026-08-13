using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class TsHeaderTests
{
    private static byte[] Packet(
        int pid = 0x100,
        int continuityCounter = 0,
        byte byte1High = 0x00,
        byte byte3High = 0x10,
        bool discontinuity = false,
        byte fill = 0x00
    )
    {
        var packet = new byte[TsPacketReader.PacketLength];
        packet[0] = TsPacketReader.SyncByte;
        packet[1] = (byte)(byte1High | ((pid >> 8) & 0x1F));
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)(byte3High | (continuityCounter & 0x0F));
        Array.Fill(packet, fill, 4, TsPacketReader.PacketLength - 4);

        if ((byte3High & 0x20) is not 0)
        {
            packet[4] = 1;
            packet[5] = discontinuity ? (byte)0x80 : (byte)0x00;
        }

        return packet;
    }

    private static TsPacket ReadOne(byte[] packet)
    {
        var reader = new TsPacketReader();
        var packets = reader.Read([.. packet, .. packet]);

        return packets[0];
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x10, true)]
    [InlineData(0x20, false)]
    [InlineData(0x30, true)]
    [InlineData(0xC0, false)]
    [InlineData(0xD0, true)]
    [InlineData(0xF0, true)]
    public void PayloadPresenceComesFromTheAdaptationFieldControl(int byte3High, bool expected)
    {
        var packet = ReadOne(Packet(byte3High: (byte)byte3High, continuityCounter: 5));

        Assert.Equal(expected, packet.HasPayload);
        Assert.Equal(5, packet.ContinuityCounter);
    }

    [Theory]
    [InlineData(0x10, false)]
    [InlineData(0x50, true)]
    [InlineData(0x90, true)]
    [InlineData(0xD0, true)]
    public void ScramblingComesFromTheTopTwoBits(int byte3High, bool expected)
    {
        Assert.Equal(expected, ReadOne(Packet(byte3High: (byte)byte3High)).Scrambled);
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x80, true)]
    public void TheTransportErrorIndicatorIsRead(int byte1High, bool expected)
    {
        Assert.Equal(expected, ReadOne(Packet(byte1High: (byte)byte1High)).TransportError);
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x40, true)]
    public void ThePayloadUnitStartIndicatorIsRead(int byte1High, bool expected)
    {
        Assert.Equal(expected, ReadOne(Packet(byte1High: (byte)byte1High)).PayloadUnitStart);
    }

    [Fact]
    public void TheTopBitsOfByteOneAreNotPartOfThePid()
    {
        Assert.Equal(0x100, ReadOne(Packet(byte1High: 0xE0)).Pid);
    }

    [Theory]
    [InlineData(0x30, true, true)]
    [InlineData(0x30, false, false)]
    [InlineData(0x10, true, false)]
    public void TheDiscontinuityIndicatorComesFromTheAdaptationField(
        int byte3High,
        bool set,
        bool expected
    )
    {
        var packet = ReadOne(
            Packet(byte3High: (byte)byte3High, discontinuity: set)
        );

        Assert.Equal(expected, packet.Discontinuity);
    }

    [Fact]
    public void PacketsWithDifferentPayloadsHashDifferently()
    {
        Assert.NotEqual(
            ReadOne(Packet(fill: 0x01)).PayloadHash,
            ReadOne(Packet(fill: 0x02)).PayloadHash
        );
    }
}
