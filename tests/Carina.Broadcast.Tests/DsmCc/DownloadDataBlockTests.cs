using Carina.Broadcast.DsmCc;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.DsmCc;

public sealed class DownloadDataBlockTests
{
    [Fact(DisplayName = "BR-BV-001: the block hands back its download module version number and data")]
    public void TheBlockHandsBackItsDownloadModuleVersionNumberAndData()
    {
        DownloadDataBlock read = Parse(new DdbWriter
        {
            DownloadId = 0x0A0B_0C0D,
            ModuleId = 0x0102,
            ModuleVersion = 0x21,
            BlockNumber = 0x0304,
            Data = [0x10, 0x20, 0x30],
        });

        Assert.Equal(0x0A0B_0C0Du, read.DownloadId);
        Assert.Equal(0x0102, read.ModuleId);
        Assert.Equal(0x21, read.ModuleVersion);
        Assert.Equal(0x0304, read.BlockNumber);
        Assert.Equal([0x10, 0x20, 0x30], read.Data.ToArray());
    }

    [Fact(DisplayName = "BR-BV-001: the data ends where the message length says even with bytes after it")]
    public void TheDataEndsWhereTheMessageLengthSaysEvenWithBytesAfterIt()
    {
        DdbWriter writer = new() { ModuleId = 1, Data = [0x01, 0x02, 0x03, 0x04], DeclaredMessageLength = 6 + 2 };

        DownloadDataBlock read = Parse(writer);

        Assert.Equal([0x01, 0x02], read.Data.ToArray());
    }

    [Fact(DisplayName = "BR-BV-001: a message length past the section is rejected")]
    public void AMessageLengthPastTheSectionIsRejected()
    {
        DdbWriter writer = new() { ModuleId = 1, Data = [0x01], DeclaredMessageLength = 6 + 2 };

        Assert.Equal(TableDefect.LoopOverrun, Defect(DownloadDataBlock.Read(CarriedSection.Of(writer.ToSection()))));
    }

    [Fact(DisplayName = "BR-BV-001: a message too short for the block header is rejected")]
    public void AMessageTooShortForTheBlockHeaderIsRejected()
    {
        DdbWriter writer = new() { ModuleId = 1, Data = [0x01], DeclaredMessageLength = 5 };

        Assert.Equal(TableDefect.SectionTooShort, Defect(DownloadDataBlock.Read(CarriedSection.Of(writer.ToSection()))));
    }

    [Fact(DisplayName = "BR-BV-001: a message that is not a download data block is rejected")]
    public void AMessageThatIsNotADownloadDataBlockIsRejected()
    {
        DdbWriter writer = new() { ModuleId = 1, MessageId = DsmCcWriter.DownloadInfoIndicationMessageId };

        Assert.Equal(TableDefect.UnexpectedMessage, Defect(DownloadDataBlock.Read(CarriedSection.Of(writer.ToSection()))));
    }

    [Fact(DisplayName = "BR-BV-001: another table id is rejected")]
    public void AnotherTableIdIsRejected()
    {
        TableRead<DownloadDataBlock> read = DownloadDataBlock.Read(CarriedSection.Of(new SectionWriter
        {
            TableId = DsmCcWriter.DownloadInfoIndicationTableId,
            Body = new DdbWriter().ToMessage(),
        }));

        Assert.Equal(TableDefect.WrongTableId, Defect(read));
    }

    [Fact(DisplayName = "BR-BV-001: a block cut short at any length before its data is rejected without throwing")]
    public void ABlockCutShortAtAnyLengthBeforeItsDataIsRejectedWithoutThrowing()
    {
        DdbWriter writer = new() { ModuleId = 1, Data = [0x01, 0x02] };
        byte[] whole = writer.ToMessage();

        for (int length = 0; length < whole.Length; length++)
        {
            Assert.IsType<TableRead<DownloadDataBlock>.Rejected>(DownloadDataBlock.Read(CarriedSection.Of(writer.Section(whole[..length]))));
        }
    }

    private static TableDefect Defect(TableRead<DownloadDataBlock> read)
        => Assert.IsType<TableRead<DownloadDataBlock>.Rejected>(read).Defect;

    private static DownloadDataBlock Parse(DdbWriter writer)
        => Assert.IsType<TableRead<DownloadDataBlock>.Parsed>(DownloadDataBlock.Read(CarriedSection.Of(writer.ToSection()))).Table;
}
