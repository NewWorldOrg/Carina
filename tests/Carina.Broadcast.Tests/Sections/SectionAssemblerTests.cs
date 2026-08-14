using Carina.Broadcast.Sections;
using Carina.Broadcast.Tests.Building;

namespace Carina.Broadcast.Tests.Sections;

public sealed class SectionAssemblerTests
{
    private const int Pid = 0x0011;
    private const int SomeTableId = 0x42;

    [Fact]
    public void ASectionInOnePacketComesBackWithTheFieldsItWasBuiltFrom()
    {
        var section = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 0x1234,
            VersionNumber = 9,
            SectionNumber = 2,
            LastSectionNumber = 5,
            Body = [0x01, 0x02, 0x03],
        }.ToBytes();

        var read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        var assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section;
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
        var body = SectionWriter.Filler(400);
        var section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(section);

        Assert.Equal(3, writer.Packets.Count);

        var assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void ASectionEndingAndAnotherBeginningInOnePacketBothComeOut()
    {
        var first = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 1,
            Body = SectionWriter.Filler(40),
        }.ToBytes();
        var second = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 2,
            Body = SectionWriter.Filler(40),
        }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(first, second);

        Assert.Single(writer.Packets);

        var read = Assemble(writer);

        Assert.Equal(
            [1, 2],
            read.Cast<SectionRead.Assembled>().Select(assembled => assembled.Section.TableIdExtension));
    }

    [Fact]
    public void ASecondSectionStartingInALaterPacketIsFoundThroughThePointer()
    {
        var first = new SectionWriter { TableId = SomeTableId, TableIdExtension = 1, Body = SectionWriter.Filler(200) }
            .ToBytes();
        var second = new SectionWriter { TableId = SomeTableId, TableIdExtension = 2, Body = SectionWriter.Filler(20) }
            .ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(first, second);

        var read = Assemble(writer);

        Assert.Equal(
            [1, 2],
            read.Cast<SectionRead.Assembled>().Select(assembled => assembled.Section.TableIdExtension));
    }

    [Fact]
    public void APacketWithAnAdjustmentFieldStillContributesItsPayload()
    {
        var body = SectionWriter.Filler(300);
        var section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(adaptationFieldLength: 30, section);

        var assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void APacketCarryingOnlyAnAdjustmentFieldDoesNotBreakContinuity()
    {
        var body = SectionWriter.Filler(400);
        var section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        var writer = new TransportStreamWriter(Pid);
        var carrier = new TransportStreamWriter(Pid).Sections(section);

        writer.Packet(0, carrier.Packets[0].AsSpan(5, 183));
        writer.AdaptationOnlyPacket(continuityCounter: 9);
        writer.Packet(null, carrier.Packets[1].AsSpan(4, 184), continuityCounter: 1);
        writer.Packet(null, carrier.Packets[2].AsSpan(4, 184), continuityCounter: 2);

        var assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(Assemble(writer))).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void AContinuityCounterThatSkippedDiscardsTheSectionInFlight()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(section);
        writer.Packets[1][3] = (byte)((writer.Packets[1][3] & 0xF0) | 0x05);

        var read = Assemble(writer);

        Assert.Equal(SectionDefect.ContinuityBroken, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.DoesNotContain(read, outcome => outcome is SectionRead.Assembled);
    }

    [Fact]
    public void ARepeatedPacketIsNotAContinuityBreak()
    {
        var body = SectionWriter.Filler(400);
        var section = new SectionWriter { TableId = SomeTableId, Body = body }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(section);
        var assembler = new SectionAssembler(Pid);
        var read = new List<SectionRead>();

        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[0]));
        read.AddRange(assembler.Push(writer.Packets[1]));
        read.AddRange(assembler.Push(writer.Packets[2]));

        var assembled = Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section;
        Assert.Equal<byte[]>(body, assembled.Body.ToArray());
    }

    [Fact]
    public void ARepeatedCounterCarryingDifferentBytesIsAContinuityBreak()
    {
        var first = new SectionWriter { TableId = SomeTableId, TableIdExtension = 1, Body = SectionWriter.Filler(20) }
            .ToBytes();
        var second = new SectionWriter { TableId = SomeTableId, TableIdExtension = 2, Body = SectionWriter.Filler(30) }
            .ToBytes();

        var writer = new TransportStreamWriter(Pid)
            .Packet(0, first, continuityCounter: 6)
            .Packet(0, second, continuityCounter: 6);

        var read = Assemble(writer);

        Assert.Equal(1, Assert.IsType<SectionRead.Assembled>(read[0]).Section.TableIdExtension);
        Assert.Equal(SectionDefect.ContinuityBroken, Assert.IsType<SectionRead.Rejected>(read[1]).Defect);
        Assert.Equal(2, Assert.IsType<SectionRead.Assembled>(read[2]).Section.TableIdExtension);
    }

    [Fact]
    public void OnlyOneRepeatOfAPacketIsPermittedBeforeItCountsAsABreak()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Sections(section);
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
        var section = new SectionWriter
        {
            TableId = SomeTableId,
            Body = SectionWriter.Filler(40),
            CorruptChecksum = true,
        }.ToBytes();

        var read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        Assert.Equal(SectionDefect.ChecksumMismatch, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void ASectionAbandonedByANewStartIsRejectedAndTheNewOneStillParses()
    {
        var abandoned = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        var following = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 7,
            Body = SectionWriter.Filler(10),
        }.ToBytes();

        var writer = new TransportStreamWriter(Pid)
            .Packet(0, abandoned.AsSpan(0, 183))
            .Packet(0, following);

        var read = Assemble(writer);

        Assert.Equal(SectionDefect.Truncated, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.Equal(7, Assert.IsType<SectionRead.Assembled>(read[1]).Section.TableIdExtension);
    }

    [Fact]
    public void ASectionLeftUnfinishedWhenTheStreamEndsIsOnlyReportedOnFlush()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(400) }.ToBytes();
        var assembler = new SectionAssembler(Pid);

        Assert.Empty(assembler.Push(new TransportStreamWriter(Pid).Packet(0, section.AsSpan(0, 183)).Packets[0]));

        var flushed = Assert.IsType<SectionRead.Rejected>(assembler.Flush());
        Assert.Equal(SectionDefect.Truncated, flushed.Defect);
        Assert.Null(assembler.Flush());
    }

    [Fact]
    public void APacketTheDemodulatorFlaggedIsNotParsed()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(40) }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Packet(0, section, transportError: true);

        var read = Assemble(writer);

        Assert.Equal(SectionDefect.TransportError, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void AScrambledPacketIsNotParsedAsIfItWerePlain()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(40) }.ToBytes();
        var writer = new TransportStreamWriter(Pid).Packet(0, section, scramblingControl: 0b10);

        var read = Assemble(writer);

        Assert.Equal(SectionDefect.Scrambled, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(4095)]
    public void ADeclaredLengthOutsideTheLongFormRangeIsRefused(int declaredLength)
    {
        var section = new SectionWriter
        {
            TableId = SomeTableId,
            Body = SectionWriter.Filler(40),
            DeclaredLength = declaredLength,
        }.ToBytes();

        var read = Assemble(new TransportStreamWriter(Pid).Sections(section));

        Assert.Equal(SectionDefect.LengthOutOfRange, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
    }

    [Fact]
    public void AShortFormSectionIsSkippedByItsOwnLengthSoTheNextOneStillParses()
    {
        var shortForm = new SectionWriter
        {
            TableId = 0x70,
            LongForm = false,
            Body = SectionWriter.Filler(5),
        }.ToBytes();
        var longForm = new SectionWriter
        {
            TableId = SomeTableId,
            TableIdExtension = 3,
            Body = SectionWriter.Filler(10),
        }.ToBytes();

        var read = Assemble(new TransportStreamWriter(Pid).Sections(shortForm, longForm));

        Assert.Equal(SectionDefect.ShortFormSection, Assert.IsType<SectionRead.Rejected>(read[0]).Defect);
        Assert.Equal(3, Assert.IsType<SectionRead.Assembled>(read[1]).Section.TableIdExtension);
    }

    [Fact]
    public void StuffingAfterTheLastSectionIsNotReadAsAnotherSection()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(10) }.ToBytes();

        Assert.Single(Assemble(new TransportStreamWriter(Pid).Sections(section)));
    }

    [Fact]
    public void APointerPastTheEndOfThePayloadIsRefused()
    {
        var writer = new TransportStreamWriter(Pid).Packet(200, [0x42, 0x00]);

        var read = Assemble(writer);

        Assert.Equal(SectionDefect.PointerOutOfRange, Assert.IsType<SectionRead.Rejected>(Assert.Single(read)).Defect);
    }

    [Fact]
    public void BytesArrivingBeforeTheFirstStartBelongToNoSection()
    {
        var section = new SectionWriter { TableId = SomeTableId, TableIdExtension = 4, Body = SectionWriter.Filler(10) }
            .ToBytes();
        var writer = new TransportStreamWriter(Pid)
            .Packet(null, SectionWriter.Filler(100))
            .Packet(0, section);

        var read = Assemble(writer);

        Assert.Equal(4, Assert.IsType<SectionRead.Assembled>(Assert.Single(read)).Section.TableIdExtension);
    }

    [Fact]
    public void APacketOnAnotherPidIsNotThisAssemblersBusiness()
    {
        var section = new SectionWriter { TableId = SomeTableId, Body = SectionWriter.Filler(10) }.ToBytes();
        var writer = new TransportStreamWriter(0x0010).Sections(section);

        Assert.Empty(new SectionAssembler(Pid).Push(writer.Packets[0]));
    }

    [Fact]
    public void AByteRunThatIsNotAPacketIsRefusedRatherThanGuessed()
    {
        var read = new SectionAssembler(Pid).Push(new byte[TransportPacket.Size]);

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
