using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ProgramAssociationTableTests
{
    [Fact(DisplayName = "BR-BD-001: the association table names the pid of each programme's map, leaving the network out")]
    public void TheAssociationTableNamesThePidOfEachProgrammesMap()
    {
        ProgramAssociationTable table = Parsed(PatWriter.Section(0x7FE0, (0, 0x0010), (1024, 0x1FC8), (1025, 0x1FC9)));

        Assert.Equal(0x7FE0, table.TransportStreamId);
        Assert.Equal([new AssociatedProgram(1024, 0x1FC8), new AssociatedProgram(1025, 0x1FC9)], table.Programs);
        Assert.Equal(0x1FC9, table.MapPidOf(1025));
        Assert.Null(table.MapPidOf(1032));
    }

    [Fact]
    public void ATableOfAnotherIdIsRefused()
    {
        TableRead<ProgramAssociationTable> read = ProgramAssociationTable.Read(CarriedSection.Of(new SectionWriter { TableId = 0x02, Body = [0x04, 0x00, 0xFF, 0xC8] }));

        Assert.Equal(TableDefect.WrongTableId, Assert.IsType<TableRead<ProgramAssociationTable>.Rejected>(read).Defect);
    }

    [Fact]
    public void ALoopThatStopsPartWayThroughAnEntryIsRefused()
    {
        TableRead<ProgramAssociationTable> read = ProgramAssociationTable.Read(CarriedSection.Of(new SectionWriter { TableId = PatWriter.TableId, Body = [0x04, 0x00, 0xFF] }));

        Assert.Equal(TableDefect.LoopOverrun, Assert.IsType<TableRead<ProgramAssociationTable>.Rejected>(read).Defect);
    }

    private static ProgramAssociationTable Parsed(byte[] section)
    {
        SectionWriter writer = new() { TableId = section[0], TableIdExtension = (section[3] << 8) | section[4], Body = section[8..^4] };

        return Assert.IsType<TableRead<ProgramAssociationTable>.Parsed>(ProgramAssociationTable.Read(CarriedSection.Of(writer))).Table;
    }
}
