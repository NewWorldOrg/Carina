using Carina.Broadcast.Sections;
using Carina.Broadcast.Tests.Building;

namespace Carina.Broadcast.Tests.Sections;

public sealed class SectionReaderTests
{
    private const int NitPid = 0x0010;
    private const int SdtPid = 0x0011;

    [Fact]
    public void EverySectionComesBackNamingThePidItArrivedOn()
    {
        var onNit = new TransportStreamWriter(NitPid)
            .Sections(new SectionWriter { TableId = 0x40, Body = SectionWriter.Filler(10) }.ToBytes())
            .Bytes;
        var onSdt = new TransportStreamWriter(SdtPid)
            .Sections(new SectionWriter { TableId = 0x42, Body = SectionWriter.Filler(10) }.ToBytes())
            .Bytes;

        var reader = new SectionReader(NitPid, SdtPid);
        var read = reader.Push(onNit).Concat(reader.Push(onSdt)).ToArray();

        Assert.Equal<int>([NitPid, SdtPid], read.Select(outcome => outcome.Pid).ToArray());
        Assert.Equal<int>(
            [0x40, 0x42],
            read.Cast<SectionRead.Assembled>().Select(outcome => outcome.Section.TableId).ToArray());
    }

    [Fact]
    public void APidNobodyAskedForIsNotAssembled()
    {
        var elsewhere = new TransportStreamWriter(0x0012)
            .Sections(new SectionWriter { TableId = 0x4E, Body = SectionWriter.Filler(10) }.ToBytes())
            .Bytes;

        var reader = new SectionReader(NitPid, SdtPid);

        Assert.Empty(reader.Push(elsewhere));
        Assert.Equal(0, reader.UnreadablePackets);
    }

    [Fact]
    public void AByteRunThatIsNotAPacketIsCountedRatherThanAttributedToAPid()
    {
        var reader = new SectionReader(NitPid);

        Assert.Empty(reader.Push(new byte[TransportPacket.Size]));
        Assert.Equal(1, reader.UnreadablePackets);
    }

    [Fact]
    public void ATrailingRunShorterThanAPacketIsCounted()
    {
        var packets = new TransportStreamWriter(NitPid)
            .Sections(new SectionWriter { TableId = 0x40, Body = SectionWriter.Filler(10) }.ToBytes())
            .Bytes;
        var reader = new SectionReader(NitPid);

        Assert.Single(reader.Push([.. packets, .. new byte[20]]));
        Assert.Equal(1, reader.UnreadablePackets);
    }

    [Fact]
    public void FlushingReportsTheUnfinishedSectionOfEveryPid()
    {
        var unfinished = new SectionWriter { TableId = 0x40, Body = SectionWriter.Filler(400) }.ToBytes();
        var reader = new SectionReader(NitPid, SdtPid);

        Assert.Empty(reader.Push(new TransportStreamWriter(NitPid).Packet(0, unfinished.AsSpan(0, 183)).Bytes));

        var flushed = reader.Flush();

        Assert.Equal(SectionDefect.Truncated, Assert.IsType<SectionRead.Rejected>(Assert.Single(flushed)).Defect);
        Assert.Equal(NitPid, flushed[0].Pid);
    }

    [Fact]
    public void RetuningResetsEveryPidSoNoHalfSectionSurvivesIt()
    {
        var unfinished = new SectionWriter { TableId = 0x40, Body = SectionWriter.Filler(400) }.ToBytes();
        var reader = new SectionReader(NitPid);

        reader.Push(new TransportStreamWriter(NitPid).Packet(0, unfinished.AsSpan(0, 183)).Bytes);
        reader.Reset();

        Assert.Empty(reader.Flush());
    }
}
