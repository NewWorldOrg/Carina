using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class PresentFollowingWatchTests
{
    private const int SomeNetwork = 32739;

    private const int SomeService = 1048;

    private static readonly WatchedService Watched = new(SomeNetwork, SomeService);

    [Fact]
    public void TheFirstSightOfAServiceIsAChangeBecauseNothingWasKnownBefore()
    {
        var watch = new PresentFollowingWatch([Watched]);

        PresentChange? change = watch.Saw(Present(eventId: 1));

        Assert.NotNull(change);
        Assert.Null(change.Was);
        Assert.True(change.IsAnotherProgramme);
        Assert.Equal(1, change.Now.EventId);
    }

    [Fact]
    public void TheSameProgrammeArrivingAgainIsNotReportedAgain()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 1));

        Assert.Null(watch.Saw(Present(eventId: 1)));
        Assert.Null(watch.Saw(Present(eventId: 1)));
    }

    [Fact]
    public void AProgrammeGivingWayToTheNextOneIsReportedAsAnotherProgramme()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 1));

        PresentChange? change = watch.Saw(Present(eventId: 2));

        Assert.NotNull(change);
        Assert.True(change.IsAnotherProgramme);
        Assert.False(change.RunsToAnotherTime);
        Assert.Equal(1, change.Was!.EventId);
    }

    [Fact]
    public void AProgrammeRunningLongerThanItSaidIsReportedWithoutBecomingAnotherOne()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 1, minutes: 30));

        PresentChange? change = watch.Saw(Present(eventId: 1, minutes: 45));

        Assert.NotNull(change);
        Assert.False(change.IsAnotherProgramme);
        Assert.False(change.StartsAtAnotherTime);
        Assert.True(change.RunsToAnotherTime);
        Assert.Equal(TimeSpan.FromMinutes(45), change.Now.Runs);
    }

    [Fact]
    public void AProgrammePushedBackIsReportedAsStartingAtAnotherTime()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 1, hours: 0x02, hour: 0x22));

        PresentChange? change = watch.Saw(Present(eventId: 1, hours: 0x01, hour: 0x23));

        Assert.NotNull(change);
        Assert.False(change.IsAnotherProgramme);
        Assert.True(change.StartsAtAnotherTime);
        Assert.False(change.RunsToAnotherTime);
    }

    [Fact]
    public void AProgrammeThatDidNotMoveAtAllReportsNoneOfTheThreeChanges()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 1));

        Assert.Null(watch.Saw(Present(eventId: 1)));
    }

    [Fact]
    public void WhatComesNextIsNotWhatIsOnNow()
    {
        var watch = new PresentFollowingWatch([Watched]);

        Assert.Null(watch.Saw(Present(eventId: 1, section: PresentFollowingWatch.FollowingSectionNumber)));
        Assert.Null(watch.PresentOn(Watched));
    }

    [Fact]
    public void AServiceNobodyAskedAboutIsNotReported()
    {
        var watch = new PresentFollowingWatch([new WatchedService(SomeNetwork, 9999)]);

        Assert.Null(watch.Saw(Present(eventId: 1)));
    }

    [Fact]
    public void TheSameServiceNumberOnAnotherNetworkIsNotTheServiceBeingWatched()
    {
        var watch = new PresentFollowingWatch([new WatchedService(1, SomeService)]);

        Assert.Null(watch.Saw(Present(eventId: 1)));
    }

    [Fact]
    public void AScheduleSectionSaysNothingAboutWhatIsOnNow()
    {
        var watch = new PresentFollowingWatch([Watched]);

        Assert.Null(watch.Saw(Present(
            eventId: 1,
            tableId: EventInformationTable.FirstScheduleActualTableId)));
    }

    [Fact]
    public void AServiceThatSaysNothingIsOnIsNotReported()
    {
        var watch = new PresentFollowingWatch([Watched]);

        Assert.Null(watch.Saw(Table(EventInformationTable.PresentFollowingActualTableId, 0, [])));
    }

    [Fact]
    public void WhatIsOnNowIsRememberedForWhoeverAsksLater()
    {
        var watch = new PresentFollowingWatch([Watched]);

        watch.Saw(Present(eventId: 7));

        Assert.Equal(7, watch.PresentOn(Watched)!.EventId);
    }

    private static EventInformationTable Present(
        int eventId,
        int minutes = 30,
        int section = PresentFollowingWatch.PresentSectionNumber,
        int tableId = EventInformationTable.PresentFollowingActualTableId,
        int hour = 0x22,
        int hours = 0)
        => Table(tableId, section, Event(eventId, minutes, hour, hours));

    private static EventInformationTable Table(int tableId, int section, byte[] events)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = SomeService,
                SectionNumber = section,
                LastSectionNumber = 1,
                Body =
                [
                    0x7F, 0xE3,
                    (byte)(SomeNetwork >> 8), (byte)(SomeNetwork & 0xFF),
                    0x01, 0x4E,
                    .. events,
                ],
            }))).Table;

    private static byte[] Event(int eventId, int minutes, int hour = 0x22, int hours = 0)
        =>
        [
            (byte)(eventId >> 8), (byte)(eventId & 0xFF),
            0xEF, 0x55, (byte)hour, 0x00, 0x00,
            (byte)hours, (byte)(hours > 0 ? 0x00 : ((minutes / 10) << 4) | (minutes % 10)), 0x00,
            0x00, 0x00,
        ];
}
