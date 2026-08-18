using Carina.Broadcast.Sections;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Sections;

public sealed class SectionAssemblerTests
{
    private const int Pid = 0x0011;
    private const int SomeTableId = 0x42;

    [Fact]
    public void ASectionInOnePacketComesBackWithTheFieldsItWasBuiltFrom()
    {
        byte[] section = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 0x1234,
            VersionNumber = 9,
            SectionNumber = 2,
            LastSectionNumber = 5,
            Body = [0x01, 0x02, 0x03],
        }.ToBytes();

        IReadOnlyList<SectionRead> read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        Section assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section;
        Assert.Equal(SomeTableId, assembled.TableId);
        Assert.Equal(0x1234, assembled.TableIdExtension);
        Assert.Equal(9, assembled.VersionNumber);
        Assert.True(assembled.IsCurrent);
        Assert.Equal(2, assembled.SectionNumber);
        Assert.Equal(5, assembled.LastSectionNumber);
        Assert.Equal<byte[]>([0x01, 0x02, 0x03], assembled.Body.ToArray());
    }

    [Fact]
    public void ASectionSpanningSeveralPacketsIsPutBackTogether()
    {
        byte[] body = SectionWriter.Filler(400);
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(section);

        Assert.Equal(3, writer.Packets.Count);

        Section assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void ASectionEndingAndAnotherBeginningInOnePacketBothComeOut()
    {
        byte[] first = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 1,
            Body = SectionWriter.Filler(40),
        }.ToBytes();
        byte[] second = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 2,
            Body = SectionWriter.Filler(40),
        }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(first, second);

        Assert.Single(writer.Packets);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(
            [1, 2],
            read.Cast<SectionRead.Assembled>().Select(assembled => assembled.Section.TableIdExtension));
    }

    [Fact]
    public void ASecondSectionStartingInALaterPacketIsFoundThroughThePointer()
    {
        byte[] first = new SectionWriter { TableId = SomeTableId, TableIdExtension = 1, Body = SectionWriter.Filler(200) }
            .ToBytes();
        byte[] second = new SectionWriter { TableId = SomeTableId, TableIdExtension = 2, Body = SectionWriter.Filler(20) }
            .ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(first, second);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(
            [1, 2],
            read.Cast<SectionRead.Assembled>().Select(assembled => assembled.Section.TableIdExtension));
    }

    [Fact]
    public void APacketWithAnAdjustmentFieldStillContributesItsPayload()
    {
        byte[] body = SectionWriter.Filler(300);
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(adaptationFieldLength: 30, section);

        Section assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void APacketCarryingOnlyAnAdjustmentFieldDoesNotBreakContinuity()
    {
        byte[] body = SectionWriter.Filler(400);
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        var writer = new TransportStreamWriter(Pid);
        TransportStreamWriter carrier = new TransportStreamWriter(Pid).Sections(section);

        writer.Packet(0, carrier.Packets[0].AsSpan(5, 183));
        writer.AdaptationOnlyPacket(continuityCounter: 9);
        writer.Packet(null, carrier.Packets[1].AsSpan(4, 184), continuityCounter: 1);
        writer.Packet(null, carrier.Packets[2].AsSpan(4, 184), continuityCounter: 2);

        Section assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void AContinuityCounterThatSkippedDiscardsTheSectionInFlight()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(section);
        writer.Packets[1][3] = (byte)((writer.Packets[1][3] & 0xF0) | 0x05);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(SectionDefect.ContinuityBroken, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.DoesNotContain(read, outcome => outcome is SectionRead.Assembled);
    }

    [Fact]
    public void ARepeatedPacketIsNotAContinuityBreak()
    {
        byte[] body = SectionWriter.Filler(400);
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(section);
        var assembler = new SectionAssembler(Pid);
        var read = new List<SectionRead>();

        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[1]));
        read.AddRange(assembler.Push(writer.Packets[2]));

        Section assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void ARepeatedCounterCarryingDifferentBytesIsAContinuityBreak()
    {
        byte[] first = new SectionWriter { TableId = SomeTableId, TableIdExtension = 1, Body = SectionWriter.Filler(20) }
            .ToBytes();
        byte[] second = new SectionWriter { TableId = SomeTableId, TableIdExtension = 2, Body = SectionWriter.Filler(30) }
            .ToBytes();

        TransportStreamWriter writer = new TransportStreamWriter(Pid)
            .Packet(0, first, continuityCounter: 6)
            .Packet(0, second, continuityCounter: 6);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(1, Assert.IsType<SectionRead.Assembled>(read[0]).Section.TableIdExtension);
        Assert.Equal(SectionDefect.ContinuityBroken, Assert.IsType<SectionRead.Rejected>(read[1]).Defect);
        Assert.Equal(2, Assert.IsType<SectionRead.Assembled>(read[2]).Section.TableIdExtension);
    }

    [Fact]
    public void OnlyOneRepeatOfAPacketIsPermittedBeforeItCountsAsABreak()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Sections(section);
        var assembler = new SectionAssembler(Pid);
        var read = new List<SectionRead>();

        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[0]));

        Assert.Equal(SectionDefect.ContinuityBroken, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void ACorruptTrailingChecksumThrowsTheWholeSectionAway()
    {
        byte[] section = new SectionWriter
        {
            TableId = SomeTableId,
            Body = SectionWriter.Filler(40),
            CorruptChecksum = true,
        }.ToBytes();

        IReadOnlyList<SectionRead> read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        Assert.Equal(SectionDefect.ChecksumMismatch, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void ASectionAbandonedByANewStartIsRejectedAndTheNewOneStillParses()
    {
        byte[] abandoned = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        byte[] following = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 7,
            Body = SectionWriter.Filler(10),
        }.ToBytes();

        TransportStreamWriter writer = new TransportStreamWriter(Pid)
            .Packet(0, abandoned.AsSpan(0, 183))
            .Packet(0, following);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(SectionDefect.Truncated, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.Equal(7, Assert.IsType<SectionRead.Assembled>(read[1]).Section.TableIdExtension);
    }

    [Fact]
    public void ASectionLeftUnfinishedWhenTheStreamEndsIsOnlyReportedOnFlush()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        var assembler = new SectionAssembler(Pid);

        Assert.Empty(assembler.Push(new TransportStreamWriter(Pid).Packet(0, section.AsSpan(0, 183)).Packets[0]));

        SectionRead.Rejected flushed = Assert.IsType<SectionRead.Rejected>(assembler.Flush());
        Assert.Equal(SectionDefect.Truncated, flushed.Defect);
        Assert.Null(assembler.Flush());
    }

    [Fact]
    public void APacketTheDemodulatorFlaggedIsNotParsed()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(40) }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, section, transportError: true);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(SectionDefect.TransportError, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void AScrambledPacketIsNotParsedAsIfItWerePlain()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(40) }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(0, section, scramblingControl: 0b10);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(SectionDefect.Scrambled, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(4095)]
    public void ADeclaredLengthOutsideTheLongFormRangeIsRefused(int declaredLength)
    {
        byte[] section = new SectionWriter
        {
            TableId = SomeTableId,
            Body = SectionWriter.Filler(40),
            DeclaredLength = declaredLength,
        }.ToBytes();

        IReadOnlyList<SectionRead> read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        Assert.Equal(SectionDefect.LengthOutOfRange, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
    }

    [Fact]
    public void AShortFormSectionIsSkippedByItsOwnLengthSoTheNextOneStillParses()
    {
        byte[] shortForm = new SectionWriter
        {
            TableId = 0x70,
            LongForm = false,
            Body = SectionWriter.Filler(5),
        }.ToBytes();
        byte[] longForm = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 3,
            Body = SectionWriter.Filler(10),
        }.ToBytes();

        IReadOnlyList<SectionRead> read = Assemble(new TransportStreamWriter(Pid).Sections(shortForm, longForm));

        Assert.Equal(SectionDefect.ShortFormSection, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.Equal(3, Assert.IsType<SectionRead.Assembled>(read[1]).Section.TableIdExtension);
    }

    [Fact]
    public void StuffingAfterTheLastSectionIsNotReadAsAnotherSection()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(10) }.ToBytes();

        Assert.Single(Assemble(new TransportStreamWriter(Pid).Sections(section)));
    }

    [Fact]
    public void APointerPastTheEndOfThePayloadIsRefused()
    {
        TransportStreamWriter writer = new TransportStreamWriter(Pid).Packet(200, [0x42, 0x00]);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(SectionDefect.PointerOutOfRange, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void BytesArrivingBeforeTheFirstStartBelongToNoSection()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, TableIdExtension = 4, Body = SectionWriter.Filler(10) }
            .ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(Pid)
            .Packet(null, SectionWriter.Filler(100))
            .Packet(0, section);

        IReadOnlyList<SectionRead> read = Assemble(writer);

        Assert.Equal(4, Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section.TableIdExtension);
    }

    [Fact]
    public void APacketOnAnotherPidIsNotThisAssemblersBusiness()
    {
        byte[] section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(10) }.ToBytes();
        TransportStreamWriter writer = new TransportStreamWriter(0x0010).Sections(section);

        Assert.Empty(new SectionAssembler(Pid).Push(writer.Packets[0]));
    }

    [Fact]
    public void AByteRunThatIsNotAPacketIsRefusedRatherThanGuessed()
    {
        IReadOnlyList<SectionRead> read = new SectionAssembler(Pid).Push(new byte[TransportPacket.Size]);

        Assert.Equal(
            SectionDefect.PacketNotSynchronised,
            Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x2000)]
    public void APidOutsideThirteenBitsIsNotAPid(int pid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectionAssembler(pid));
    }

    private static IReadOnlyList<SectionRead> Assemble(TransportStreamWriter writer)
    {
        var assembler = new SectionAssembler(Pid);

        return writer.Packets.SelectMany(packet => assembler.Push(packet)).ToList();
    }
}
