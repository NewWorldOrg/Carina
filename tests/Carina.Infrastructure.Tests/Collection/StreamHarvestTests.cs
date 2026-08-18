using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;

namespace Carina.Infrastructure.Tests.Collection;

public sealed class StreamHarvestTests
{
    private const int SomeService = 1024;

    private const int FirstBasic = EventInformationTable.FirstScheduleActualTableId;

    private const int LastBasic = FirstBasic + 1;

    private const int FirstExtended = FirstBasic + 8;

    private const int LastExtended = FirstExtended + 1;

    [Fact]
    public void AVisitTheDriverCutShortIsNamedAsSuchWhateverItGathered()
    {
        var harvest = new StreamHarvest();

        Gather(harvest, FirstBasic, LastBasic);
        Gather(harvest, LastBasic, LastBasic);

        Assert.Equal(VisitOutcome.Interrupted, harvest.Conclude(interrupted: true, anyBytes: true).Outcome);
    }

    [Fact]
    public void AVisitThatSawNoBytesAtAllIsNamedAsSuch()
        => Assert.Equal(
            VisitOutcome.NoBytes,
            new StreamHarvest().Conclude(interrupted: false, anyBytes: false).Outcome);

    [Fact]
    public void AVisitThatGatheredTheBasicTablesMaySayGoodbyeToTheTuner()
    {
        var harvest = new StreamHarvest();

        Assert.False(harvest.CanLetGo);

        Gather(harvest, FirstBasic, LastBasic);

        Assert.False(harvest.CanLetGo);

        Gather(harvest, LastBasic, LastBasic);

        Assert.True(harvest.CanLetGo);
        Assert.Equal(VisitOutcome.BasicOnly, harvest.Conclude(interrupted: false, anyBytes: true).Outcome);
    }

    [Fact]
    public void AVisitThatGatheredTheDetailedTablesTooHasNothingLeftToWaitFor()
    {
        var harvest = new StreamHarvest();

        Gather(harvest, FirstBasic, LastBasic);
        Gather(harvest, LastBasic, LastBasic);
        Gather(harvest, FirstExtended, LastExtended);
        Gather(harvest, LastExtended, LastExtended);

        Assert.Equal(VisitOutcome.Complete, harvest.Conclude(interrupted: false, anyBytes: true).Outcome);
    }

    [Fact]
    public void AVisitThatSawBytesButNeverEnoughIsNamedIncomplete()
    {
        var harvest = new StreamHarvest();

        Gather(harvest, FirstBasic, LastBasic, segments: 2);

        Assert.False(harvest.CanLetGo);
        Assert.Equal(VisitOutcome.Incomplete, harvest.Conclude(interrupted: false, anyBytes: true).Outcome);
    }

    [Fact]
    public void EveryTableItReadIsKeptForWhoeverWritesThemDown()
    {
        var harvest = new StreamHarvest();

        Gather(harvest, FirstBasic, LastBasic, segments: 3);

        Assert.Equal(3, harvest.Conclude(interrupted: false, anyBytes: true).Tables.Count);
    }

    [Fact]
    public void BytesThatAreNotEvenPacketsAreCountedRatherThanThrown()
    {
        var harvest = new StreamHarvest();

        harvest.Push(new byte[188]);

        Assert.Equal(1, harvest.UnreadablePackets);
    }

    private static void Gather(StreamHarvest harvest, int tableId, int lastTableId, int segments = 4)
    {
        for (var segment = 0; segment < segments; segment++)
        {
            var section = segment * ScheduleProgress.SectionsPerSegment;

            harvest.Push(Packets(tableId, lastTableId, section, section));
        }
    }

    private static byte[] Packets(int tableId, int lastTableId, int section, int segmentLast)
        => [.. new TransportStreamWriter(EventInformationTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = SomeService,
                SectionNumber = section,
                LastSectionNumber = 31,
                Body =
                [
                    0x7F, 0xE3, 0x7F, 0xE3,
                    (byte)segmentLast,
                    (byte)lastTableId,
                ],
            }.ToBytes())
            .Packets
            .SelectMany(packet => packet.ToArray())];
}
