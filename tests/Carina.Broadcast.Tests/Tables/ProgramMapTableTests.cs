using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ProgramMapTableTests
{
    private const int SomeProgramme = 0x0400;

    private const int SomePcrPid = 0x01FF;

    [Fact]
    public void TheTableHandsBackEveryStreamWithItsTypePidAndDescriptors()
    {
        ProgramMapTable table = Parse(new PmtWriter
        {
            ProgramNumber = SomeProgramme,
            PcrPid = SomePcrPid,
            Streams =
            [
                PmtWriter.Stream(0x02, 0x0111, PsiDescriptorWriter.StreamIdentifier(0x00)),
                PmtWriter.Stream(PmtWriter.DsmCcSections, 0x0140, PsiDescriptorWriter.StreamIdentifier(0x40)),
            ],
        });

        Assert.Equal(SomeProgramme, table.ProgramNumber);
        Assert.Equal(SomePcrPid, table.PcrPid);
        Assert.Equal([0x02, PmtWriter.DsmCcSections], table.Streams.Select(stream => stream.StreamType));
        Assert.Equal([0x0111, 0x0140], table.Streams.Select(stream => stream.Pid));
        Assert.Equal(DescriptorTags.StreamIdentifier, table.Streams[1].Descriptors.Single().Tag);
    }

    [Fact]
    public void ADescriptorOfTheProgrammeDoesNotMoveWhereTheStreamsStart()
    {
        ProgramMapTable table = Parse(new PmtWriter
        {
            ProgramNumber = SomeProgramme,
            Descriptors = DescriptorWriter.Of(0x09, 0x00, 0x05, 0xE1, 0x01),
            Streams = [PmtWriter.Stream(PmtWriter.DsmCcSections, 0x0140, [])],
        });

        Assert.Single(table.Descriptors);
        Assert.Equal(0x0140, table.Streams.Single().Pid);
    }

    [Fact]
    public void AProgrammeWithoutAClockReferenceHasNoPcrPid()
    {
        ProgramMapTable table = Parse(new PmtWriter { ProgramNumber = SomeProgramme, PcrPid = PmtWriter.NoPcr });

        Assert.Null(table.PcrPid);
    }

    [Fact]
    public void ATableOfAnotherIdIsRejected()
    {
        TableRead<ProgramMapTable> read = ProgramMapTable.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = 0x42,
            Body = new ByteWriter().Word(0xFFFF).Word(0xF000).ToArray(),
        }));

        Assert.Equal(TableDefect.WrongTableId, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ASectionTooShortForTheFixedFieldsIsRejected(int length)
    {
        TableRead<ProgramMapTable> read = ReadBody(new byte[length]);

        Assert.Equal(TableDefect.SectionTooShort, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    [Fact]
    public void AStreamWhoseDescriptorsRunPastTheSectionIsRejected()
    {
        byte[] body = new ByteWriter()
            .Word(0xE000 | SomePcrPid)
            .Word(0xF000)
            .Byte(PmtWriter.DsmCcSections)
            .Word(0xE140)
            .Word(0xF000 | 0x20)
            .Run(PsiDescriptorWriter.StreamIdentifier(0x40))
            .ToArray();

        TableRead<ProgramMapTable> read = ReadBody(body);

        Assert.Equal(TableDefect.LoopOverrun, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    [Fact]
    public void AStreamHeaderCutShortIsRejected()
    {
        byte[] body = new ByteWriter()
            .Word(0xE000 | SomePcrPid)
            .Word(0xF000)
            .Byte(PmtWriter.DsmCcSections)
            .Word(0xE140)
            .ToArray();

        TableRead<ProgramMapTable> read = ReadBody(body);

        Assert.Equal(TableDefect.LoopOverrun, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    [Fact]
    public void ProgrammeDescriptorsRunningPastTheSectionAreRejected()
    {
        byte[] body = new ByteWriter().Word(0xE000 | SomePcrPid).Word(0xF000 | 0x10).ToArray();

        TableRead<ProgramMapTable> read = ReadBody(body);

        Assert.Equal(TableDefect.LoopOverrun, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    [Fact]
    public void ABrokenDescriptorInsideAStreamIsRejected()
    {
        byte[] descriptors = DescriptorWriter.Overrunning(DescriptorTags.StreamIdentifier, 4, 0x40);

        TableRead<ProgramMapTable> read = ReadBody(new ByteWriter()
            .Word(0xE000 | SomePcrPid)
            .Word(0xF000)
            .Run(PmtWriter.Stream(PmtWriter.DsmCcSections, 0x0140, descriptors))
            .ToArray());

        Assert.Equal(TableDefect.MalformedDescriptor, Assert.IsType<TableRead<ProgramMapTable>.Rejected>(read).Defect);
    }

    private static TableRead<ProgramMapTable> ReadBody(byte[] body)
        => ProgramMapTable.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = PmtWriter.TableId,
            TableIdExtension = SomeProgramme,
            Body = body,
        }));

    private static ProgramMapTable Parse(PmtWriter writer)
        => Assert.IsType<TableRead<ProgramMapTable>.Parsed>(ProgramMapTable.Read(CarriedSection.Of(writer.ToSection()))).Table;
}
