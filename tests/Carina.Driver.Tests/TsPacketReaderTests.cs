using Carina.Driver.Transport;

namespace Carina.Driver.Tests;

/// <summary>
/// Finding packet boundaries in a stream that may start anywhere.
/// </summary>
/// <remarks>
/// A tuner hands over bytes, not packets: the first read can land mid-packet, and a
/// hardware hiccup can leave a gap. Everything downstream — the continuity count,
/// the recording, the quality figure — is measured per packet, so a reader that
/// loses alignment silently would report a healthy stream while writing rubbish.
/// </remarks>
public sealed class TsPacketReaderTests
{
    private const int PacketLength = 188;

    private static byte[] Packet(int pid, int continuityCounter, byte fill = 0x00)
    {
        var packet = new byte[PacketLength];
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

    // A read that arrives split across buffers is the normal case, not an edge one.
    [Fact]
    public void KeepsAPartialPacketUntilTheRestArrives()
    {
        var reader = new TsPacketReader();
        var packet = Packet(0x100, 3);

        Assert.Empty(reader.Read(packet.AsSpan(0, 100).ToArray()));

        var completed = reader.Read(packet.AsSpan(100).ToArray()).ToList();

        Assert.Single(completed);
        Assert.Equal(3, completed[0].ContinuityCounter);
    }

    // The first bytes off a tuner are rarely a packet boundary.
    [Fact]
    public void FindsTheFirstBoundaryInAStreamThatStartsMidPacket()
    {
        var reader = new TsPacketReader();
        var stream = Concat([0x11, 0x22, 0x33], Packet(0x101, 5), Packet(0x101, 6));

        var packets = reader.Read(stream).ToList();

        Assert.Equal(2, packets.Count);
        Assert.Equal(0x101, packets[0].Pid);
        Assert.Equal(5, packets[0].ContinuityCounter);
    }

    // A sync byte inside the payload is not a boundary. Only the byte that repeats
    // every 188 is, which is why alignment is confirmed against the next packet.
    [Fact]
    public void DoesNotMistakeAPayloadByteForABoundary()
    {
        var reader = new TsPacketReader();
        var decoy = Packet(0x102, 0, fill: 0x47);
        var stream = Concat([0x00], decoy, Packet(0x102, 1), Packet(0x102, 2));

        var packets = reader.Read(stream).ToList();

        Assert.Equal(3, packets.Count);
        Assert.All(packets, packet => Assert.Equal(0x102, packet.Pid));
        Assert.Equal([0, 1, 2], packets.Select(packet => packet.ContinuityCounter));
    }

    [Fact]
    public void RegainsAlignmentAfterTheStreamBreaks()
    {
        var reader = new TsPacketReader();
        reader.Read(Packet(0x103, 0));

        var stream = Concat([0xFF, 0xFF, 0xFF], Packet(0x103, 4), Packet(0x103, 5));
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
    public void ReadsTheNullPacketPid()
    {
        var reader = new TsPacketReader();

        var packet = Assert.Single(reader.Read(Packet(0x1FFF, 0)));

        Assert.Equal(0x1FFF, packet.Pid);
        Assert.True(packet.IsNull);
    }
}
