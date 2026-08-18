using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

public sealed class TsPacketReaderTests
{
    private const int PacketLength = 188;

    private static byte[] Packet(int pid, int continuityCounter, byte fill = 0x00)
    {
        byte[] packet = new byte[PacketLength];
        packet[0] = 0x47;
        packet[1] = (byte)((pid >> 8) & 0x1F);
        packet[2] = (byte)(pid & 0xFF);
        packet[3] = (byte)(0x10 | (continuityCounter & 0x0F));
        Array.Fill(packet, fill, 4, PacketLength - 4);

        return packet;
    }

    private static byte[] Concat(params byte[][] parts) =>
        parts.SelectMany(part => part).ToArray();

    [Fact]
    public void ReadsWholePacketsFromAnAlignedStream()
    {
        var reader = new TsPacketReader();

        var packets = reader.Read(Concat(Packet(0x100, 0), Packet(0x100, 1))).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal(0x100, packets[0].Pid);
        Assert.Equal(0, packets[0].ContinuityCounter);
        Assert.Equal(1, packets[1].ContinuityCounter);
    }

    [Fact]
    public void KeepsAPartialPacketUntilTheRestArrives()
    {
        var reader = new TsPacketReader();
        byte[] packet = Packet(0x100, 3);

        Assert.Empty(reader.Read(packet.AsSpan(0, 100).ToArray()));

        var completed = reader.Read(packet.AsSpan(100).ToArray()).ToList();

        Assert.Single(completed);
        Assert.Equal(3, completed[0].ContinuityCounter);
    }

    [Fact]
    public void FindsTheFirstBoundaryInAStreamThatStartsMidPacket()
    {
        var reader = new TsPacketReader();
        byte[] stream = Concat([0x11, 0x22, 0x33], Packet(0x101, 5), Packet(0x101, 6));

        var packets = reader.Read(stream).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal(0x101, packets[0].Pid);
        Assert.Equal(5, packets[0].ContinuityCounter);
    }

    [Fact]
    public void DoesNotMistakeAPayloadByteForABoundary()
    {
        var reader = new TsPacketReader();
        byte[] decoy = Packet(0x102, 0, fill: 0x47);
        byte[] stream = Concat([0x00], decoy, Packet(0x102, 1), Packet(0x102, 2));

        var packets = reader.Read(stream).ToList();

        Assert.Equal(3, packets.Count);
        Assert.All(packets, packet => Assert.Equal(0x102, packet.Pid));
        Assert.Equal([0, 1, 2], packets.Select(packet => packet.ContinuityCounter));
    }

    [Fact]
    public void RegainsAlignmentAfterTheStreamBreaks()
    {
        var reader = new TsPacketReader();
        reader.Read(Concat(Packet(0x103, 0), Packet(0x103, 1)));

        byte[] stream = Concat([0xFF, 0xFF, 0xFF], Packet(0x103, 4), Packet(0x103, 5));
        var packets = reader.Read(stream).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal(4, packets[0].ContinuityCounter);
        Assert.Equal(1, reader.ResyncCount);
    }

    [Fact]
    public void CountsTheBytesItThrewAway()
    {
        var reader = new TsPacketReader();

        reader.Read(Concat([0x11, 0x22], Packet(0x104, 0), Packet(0x104, 1)));

        Assert.Equal(2, reader.DiscardedBytes);
    }

    [Fact]
    public void AStreamWithNoBoundaryIsNotKeptForever()
    {
        var reader = new TsPacketReader();

        for (int read = 0; read < 64; read++)
        {
            Assert.Empty(reader.Read(new byte[4096]));
        }

        Assert.True(reader.DiscardedBytes > 4096 * 60);
    }

    [Fact]
    public void APacketIsNotEmittedFromAnAlignmentNothingConfirmed()
    {
        var reader = new TsPacketReader();
        byte[] decoy = new byte[PacketLength];
        decoy[13] = 0x47;

        Assert.Empty(reader.Read(decoy));
    }

    [Fact]
    public void APacketFromAnUnconfirmedBoundaryIsMarked()
    {
        var reader = new TsPacketReader();

        TsPacket packet = Assert.Single(reader.Read(Packet(0x100, 0)));

        Assert.True(packet.Provisional);
    }

    [Fact]
    public void APacketFromAConfirmedBoundaryIsNotMarked()
    {
        var reader = new TsPacketReader();

        IReadOnlyList<TsPacket> packets = reader.Read(Concat(Packet(0x100, 0), Packet(0x100, 1)));

        Assert.All(packets, packet => Assert.False(packet.Provisional));
    }

    [Fact]
    public void AByteThatDisprovesTheBoundaryDropsTheAlignment()
    {
        var reader = new TsPacketReader();
        byte[] first = new byte[PacketLength];
        first[0] = 0x47;
        byte[] second = new byte[PacketLength];

        Assert.True(reader.Read(first)[0].Provisional);
        Assert.Empty(reader.Read(second));
        Assert.True(reader.DiscardedBytes > 0);
    }

    [Fact]
    public void ARealBoundaryStillReadsAfterAFalseOne()
    {
        var reader = new TsPacketReader();
        byte[] noise = new byte[40];
        noise[0] = 0x47;

        var packets = reader
            .Read(Concat(noise, Packet(0x105, 0), Packet(0x105, 1)))
            .ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal(0x105, packets[0].Pid);
    }

    [Fact]
    public void AStrideThisReaderCannotParseIsVisible()
    {
        var reader = new TsPacketReader();
        byte[] packet = Packet(0x100, 0);

        for (int count = 0; count < 20; count++)
        {
            reader.Read([.. packet, .. new byte[16]]);
        }

        Assert.True(reader.LooksLikeAnotherStride);
    }

    [Fact]
    public void ReadsTheNullPacketPid()
    {
        var reader = new TsPacketReader();

        IReadOnlyList<TsPacket> packets = reader.Read(Concat(Packet(0x1FFF, 0), Packet(0x1FFF, 1)));

        Assert.Equal(0x1FFF, packets[0].Pid);
        Assert.True(packets[0].IsNull);
    }
}
