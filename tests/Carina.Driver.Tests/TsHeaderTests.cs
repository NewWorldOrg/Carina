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
        byte[] packet = new byte[TsPacketReader.PacketLength];
        packet[0] = TsPacketReader.SyncByte;
        packet[1] = (byte)(byte1High | ((pid >> 8) & 0x1F));
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)(byte3High | (continuityCounter & 0x0F));
        Array.Fill(packet, fill, 4, TsPacketReader.PacketLength - 4);

        if ((byte3High & 0x20) is not 0)
        {
            packet[4] = 7;
            packet[5] = (byte)((discontinuity ? 0x80 : 0x00) | 0x10);
            Array.Fill(packet, (byte)0x5A, 6, 6);
        }

        return packet;
    }

    private static TsPacket ReadOne(byte[] packet)
    {
        var reader = new TsPacketReader();
        IReadOnlyList<TsPacket> packets = reader.Read([.. packet, .. packet]);

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
        TsPacket packet = ReadOne(Packet(byte3High: (byte)byte3High, continuityCounter: 5));

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
        TsPacket packet = ReadOne(
            Packet(byte3High: (byte)byte3High, discontinuity: set)
        );

        Assert.Equal(expected, packet.Discontinuity);
    }

    [Fact]
    public void ARewrittenAdaptationFieldDoesNotChangeTheHash()
    {
        byte[] first = Packet(byte3High: 0x30, fill: 0x11);
        byte[] second = (byte[])first.Clone();
        second[8] = 0x99;

        Assert.Equal(ReadOne(first).PayloadHash, ReadOne(second).PayloadHash);
    }

    [Fact]
    public void PacketsWithDifferentPayloadsHashDifferently()
    {
        Assert.NotEqual(
            ReadOne(Packet(fill: 0x01)).PayloadHash,
            ReadOne(Packet(fill: 0x02)).PayloadHash
        );
    }

    private static byte[] WithClock(long baseValue, int extension = 0, byte adaptationLength = 7, bool flagged = true)
    {
        byte[] packet = new byte[TsPacketReader.PacketLength];
        packet[0] = TsPacketReader.SyncByte;
        packet[1] = 0x01;
        packet[2] = 0x00;
        packet[3] = 0x30;
        packet[4] = adaptationLength;
        packet[5] = (byte)(flagged ? 0x10 : 0x00);
        packet[6] = (byte)(baseValue >> 25);
        packet[7] = (byte)((baseValue >> 17) & 0xFF);
        packet[8] = (byte)((baseValue >> 9) & 0xFF);
        packet[9] = (byte)((baseValue >> 1) & 0xFF);
        int lowest = (int)(baseValue & 1);
        packet[10] = (byte)((lowest << 7) | 0x7E | ((extension >> 8) & 0x01));
        packet[11] = (byte)(extension & 0xFF);

        return packet;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(90_000)]
    [InlineData(123_456_789)]
    [InlineData(8_589_934_591)]
    public void TheProgrammeClockIsReadFromTheAdaptationField(long reference)
    {
        Assert.Equal(reference, ReadOne(WithClock(reference)).Pcr);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(299)]
    public void TheNineBitExtensionIsNotPartOfTheClock(int extension)
    {
        Assert.Equal(90_000, ReadOne(WithClock(90_000, extension)).Pcr);
    }

    [Fact]
    public void AnAdaptationFieldThatCarriesNoClockSaysNothingAboutTheTime()
    {
        Assert.Null(ReadOne(WithClock(90_000, flagged: false)).Pcr);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void AClockFlaggedInAFieldTooShortToHoldItIsNotRead(byte adaptationLength)
    {
        Assert.Null(ReadOne(WithClock(90_000, adaptationLength: adaptationLength)).Pcr);
    }

    [Fact]
    public void APacketWithNoAdaptationFieldSaysNothingAboutTheTime()
    {
        Assert.Null(ReadOne(Packet(byte3High: 0x10)).Pcr);
    }

    [Fact]
    public void AnEmptyAdaptationFieldSaysNothingAboutTheTime()
    {
        byte[] packet = WithClock(90_000);
        packet[4] = 0;

        Assert.Null(ReadOne(packet).Pcr);
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0x30)]
    public void APacketCarryingOnlyAnAdaptationFieldStillSaysTheTimeAndTheBreak(int byte3High)
    {
        byte[] packet = WithClock(4_500_000);
        packet[3] = (byte)byte3High;
        packet[5] = 0x90;

        TsPacket read = ReadOne(packet);

        Assert.Equal(4_500_000, read.Pcr);
        Assert.True(read.Discontinuity);
        Assert.Equal(byte3High is 0x30, read.HasPayload);
    }
}
