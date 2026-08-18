using Carina.Broadcast.Descriptors;
using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class EventInformationTableTests
{
    private const int SomeService = 1024;

    [Fact]
    public void ASectionWithoutRoomForItsOwnHeaderIsRefused()
    {
        Assert.Equal(TableDefect.SectionTooShort, Refusal([0x00, 0x01, 0x02]));
    }

    [Fact]
    public void AnEventLoopThatRunsPastTheSectionIsRefused()
    {
        Assert.Equal(
            TableDefect.LoopOverrun,
            Refusal([.. Header(), 0x00, 0x01, 0xEF, 0x55, 0x22, 0x57, 0x00, 0x00, 0x03, 0x00, 0x00]));
    }

    [Fact]
    public void AnEventClaimingMoreDescriptorsThanItCarriesIsRefused()
    {
        Assert.Equal(
            TableDefect.LoopOverrun,
            Refusal([.. Header(), .. Event(descriptorsLength: 0x40)]));
    }

    [Fact]
    public void AnEventWhoseStartIsNotAReadableTimeIsDroppedAndCounted()
    {
        EventInformationTable table = Parsed([.. Header(), .. Event(hour: 0x2A), .. Event()]);

        Assert.Equal(1, table.DiscardedEvents);
        Assert.Single(table.Events);
    }

    [Fact]
    public void AStartTheBroadcastLeavesOpenCostsThatEventAndNoOther()
    {
        EventInformationTable table = Parsed([.. Header(), .. Event(startUndefined: true), .. Event()]);

        Assert.Equal(1, table.DiscardedEvents);
        Assert.Single(table.Events);
    }

    [Fact]
    public void ASectionWhoseEventsAreAllReadableCountsNoneDiscarded()
    {
        Assert.Equal(0, Parsed([.. Header(), .. Event()]).DiscardedEvents);
    }

    [Fact]
    public void AnEventCarryingAHalfDescriptorIsRefused()
    {
        Assert.Equal(
            TableDefect.MalformedDescriptor,
            Refusal([.. Header(), .. Event(descriptorsLength: 1), 0x4D]));
    }

    [Fact]
    public void AnEventWithNoDescriptorsIsStillAnEvent()
    {
        EventInformationTable table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event()],
            }))).Table;

        DescribedEvent carried = Assert.Single(table.Events);

        Assert.Equal(SomeService, table.ServiceId);
        Assert.Equal(1, carried.EventId);
        Assert.Empty(carried.Descriptors);
        Assert.Null(carried.Described);
    }

    [Fact]
    public void AnEventWithoutAnEndIsCarriedWithoutOneRatherThanGuessed()
    {
        EventInformationTable table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(openEnded: true)],
            }))).Table;

        DescribedEvent carried = Assert.Single(table.Events);

        Assert.Null(carried.Runs);
        Assert.Null(carried.EndsAt);
    }

    [Fact]
    public void ARunningStatusThisLibraryDoesNotKnowIsCarriedAsUndefined()
    {
        EventInformationTable table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(status: 7)],
            }))).Table;

        Assert.Equal(RunningStatus.Undefined, Assert.Single(table.Events).Status);
    }

    [Fact]
    public void EverySchedulingTableIdCarriesEvents()
    {
        Assert.True(EventInformationTable.CarriesEvents(EventInformationTable.PresentFollowingActualTableId));

        for (int tableId = EventInformationTable.FirstScheduleActualTableId;
            tableId <= EventInformationTable.LastScheduleActualTableId;
            tableId++)
        {
            Assert.True(EventInformationTable.CarriesEvents(tableId));
        }

        Assert.False(EventInformationTable.CarriesEvents(ServiceDescriptionTable.ActualStreamTableId));
    }

    [Fact]
    public void ASummaryTheBroadcastCutShortLeavesTheEventUndescribed()
    {
        byte[] descriptor = new byte[] { DescriptorTags.ShortEvent, 0x07, 0x6A, 0x70, 0x6E, 0x02, 0x41, 0x42, 0x08 };

        EventInformationTable table = Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = [.. Header(), .. Event(descriptorsLength: descriptor.Length), .. descriptor],
            }))).Table;

        DescribedEvent carried = Assert.Single(table.Events);

        Assert.Single(carried.Descriptors);
        Assert.Null(carried.Described);
    }

    private static byte[] Header() => [0x7F, 0xE3, 0x7F, 0xE3, 0x00, 0x4E];

    private static EventInformationTable Parsed(byte[] body)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = body,
            }))).Table;

    private static byte[] Event(
        int hour = 0x22,
        int status = 0,
        int descriptorsLength = 0,
        bool openEnded = false,
        bool startUndefined = false)
        =>
        [
            0x00, 0x01,
            startUndefined ? (byte)0xFF : (byte)0xEF,
            startUndefined ? (byte)0xFF : (byte)0x55,
            startUndefined ? (byte)0xFF : (byte)hour,
            startUndefined ? (byte)0xFF : (byte)0x57,
            startUndefined ? (byte)0xFF : (byte)0x00,
            openEnded ? (byte)0xFF : (byte)0x00, openEnded ? (byte)0xFF : (byte)0x03, openEnded ? (byte)0xFF : (byte)0x00,
            (byte)((status << 5) | ((descriptorsLength >> 8) & 0x0F)), (byte)(descriptorsLength & 0xFF),
        ];

    private static TableDefect Refusal(byte[] body)
        => Assert.IsType<TableRead<EventInformationTable>.Rejected>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = EventInformationTable.PresentFollowingActualTableId,
                TableIdExtension = SomeService,
                Body = body,
            }))).Defect;
}
