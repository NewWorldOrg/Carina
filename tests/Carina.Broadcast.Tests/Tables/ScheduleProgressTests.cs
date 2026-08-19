using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ScheduleProgressTests
{
    private const int SomeNetwork = 32739;

    private const int SomeStream = 32739;

    private const int SomeService = 1024;

    private const int AnotherService = 1025;

    private static readonly ScheduledService Service = new(SomeNetwork, SomeStream, SomeService);

    private static readonly ScheduledService Another = new(SomeNetwork, SomeStream, AnotherService);

    private const int FirstBasic = EventInformationTable.FirstScheduleActualTableId;

    private const int LastBasic = FirstBasic + 1;

    private const int FirstExtended = FirstBasic + 8;

    private const int LastExtended = FirstExtended + 1;

    [Fact]
    public void ATableIsNotWholeUntilEverySegmentHasBeenSeen()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 3, lastSection: 31);

        Assert.False(progress.IsWhole(Service, FirstBasic));
        Assert.Equal([3], progress.SegmentsAwaited(Service, FirstBasic));
    }

    [Fact]
    public void ATableWhoseEverySegmentArrivedAwaitsNothing()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, FirstBasic, segments: 4, lastSection: 31);

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.True(progress.IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void ASegmentCarryingSeveralSectionsIsNotWholeUntilAllOfThemArrive()
    {
        var progress = new ScheduleProgress(Midnight());

        progress.Saw(Table(FirstBasic, FirstBasic, section: 0, segmentLast: 2, lastSection: 7));

        Assert.Equal([0], progress.SegmentsAwaited(Service, FirstBasic));

        progress.Saw(Table(FirstBasic, FirstBasic, section: 1, segmentLast: 2, lastSection: 7));
        progress.Saw(Table(FirstBasic, FirstBasic, section: 2, segmentLast: 2, lastSection: 7));

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
    }

    [Fact]
    public void TheBasicTablesBeingWholeIsEnoughToLetGoOfTheTuner()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.BasicOnly, progress.Completeness);
    }

    [Fact]
    public void OnlyOneOfTheBasicTablesIsNotEnough()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void TheDetailedTablesOnTopOfTheBasicOnesMeanThereIsNothingLeftToWaitFor()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, FirstExtended, LastExtended, segments: 4, lastSection: 31);
        Gather(progress, LastExtended, LastExtended, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Complete, progress.Completeness);
    }

    [Fact]
    public void TheDetailedTablesAloneDoNotMakeTheScheduleComplete()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstExtended, LastExtended, segments: 4, lastSection: 31);
        Gather(progress, LastExtended, LastExtended, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void EachTableKeepsItsOwnVersionSoOneMovingOnDoesNotUndoTheOthers()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31, version: 9);

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.Empty(progress.SegmentsAwaited(Service, LastBasic));
        Assert.Equal(ScheduleCompleteness.BasicOnly, progress.Completeness);
    }

    [Fact]
    public void ATableThatMovesToANewVersionStartsOverOnItsOwn()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);

        progress.Saw(Table(FirstBasic, LastBasic, section: 0, segmentLast: 0, lastSection: 31, version: 9));

        Assert.Equal([1, 2, 3], progress.SegmentsAwaited(Service, FirstBasic));
        Assert.Empty(progress.SegmentsAwaited(Service, LastBasic));
        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void WhatIsOnNowAndNextIsNotPartOfTheSchedule()
    {
        var progress = new ScheduleProgress(Midnight());

        progress.Saw(Table(
            EventInformationTable.PresentFollowingActualTableId,
            EventInformationTable.PresentFollowingActualTableId,
            section: 0,
            segmentLast: 0,
            lastSection: 1));

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
        Assert.Empty(progress.SegmentsAwaited(Service, EventInformationTable.PresentFollowingActualTableId));
    }

    [Fact]
    public void TwoServicesOnTheOneStreamKeepTheirOwnProgress()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, FirstBasic, LastBasic, segments: 2, lastSection: 31, service: AnotherService);

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.Equal([2, 3], progress.SegmentsAwaited(Another, FirstBasic));
        Assert.Equal(ScheduleCompleteness.BasicOnly, progress.CompletenessOf(Service));
        Assert.Equal(ScheduleCompleteness.Incomplete, progress.CompletenessOf(Another));
    }

    [Fact]
    public void TheStreamIsOnlyAsFinishedAsItsLeastFinishedService()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, FirstBasic, LastBasic, segments: 2, lastSection: 31, service: AnotherService);

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
        Assert.Equal([Service, Another], progress.Services);
    }

    [Fact]
    public void OneServiceMovingToANewVersionDoesNotUndoAnother()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31, version: 9, service: AnotherService);

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.Empty(progress.SegmentsAwaited(Another, FirstBasic));
    }

    [Fact]
    public void ATableNamingALastTableBeforeItselfIsNotTakenAsFinished()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, LastBasic, FirstBasic, segments: 4, lastSection: 31);

        Assert.False(progress.IsWhole(Service, LastBasic));
    }

    [Fact]
    public void ATableNamingALastTableBeyondTheScheduleIsNotTakenAsFinished()
    {
        var progress = new ScheduleProgress(Midnight());

        Gather(progress, FirstBasic, 0xFF, segments: 4, lastSection: 31);

        Assert.False(progress.IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void ATableNeverSeenAwaitsNothingBecauseNothingIsKnownOfIt()
    {
        Assert.Empty(new ScheduleProgress(Midnight()).SegmentsAwaited(Service, FirstBasic));
        Assert.False(new ScheduleProgress(Midnight()).IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void TheHoursThatHaveAlreadyGoneByAreNotWaitedFor()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 15, 20, 0));

        Gather(progress, FirstBasic, FirstBasic, segments: 32, lastSection: 255, from: 5);

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.True(progress.IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void TheSegmentWhoseHoursAreRunningIsStillWaitedFor()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 15, 0, 0));

        Gather(progress, FirstBasic, FirstBasic, segments: 32, lastSection: 255, from: 6);

        Assert.Equal([5], progress.SegmentsAwaited(Service, FirstBasic));
        Assert.False(progress.IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void TheSegmentsStillToComeAreWaitedForHoweverLateInTheDayItIs()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 23, 59, 59));

        Gather(progress, FirstBasic, FirstBasic, segments: 32, lastSection: 255, from: 9);

        Assert.Equal([7, 8], progress.SegmentsAwaited(Service, FirstBasic));
    }

    [Fact]
    public void ATableForTheDaysBeyondTheFirstNeverGoesStale()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 23, 59, 59));

        Gather(progress, LastBasic, LastBasic, segments: 32, lastSection: 255, from: 1);

        Assert.Equal([0], progress.SegmentsAwaited(Service, LastBasic));
    }

    [Fact]
    public void JustAfterMidnightNoSegmentHasGoneStaleYet()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 20, 0, 0, 1));

        Gather(progress, FirstBasic, FirstBasic, segments: 4, lastSection: 31, from: 1);

        Assert.Equal([0], progress.SegmentsAwaited(Service, FirstBasic));
    }

    [Fact]
    public void ASegmentStopsBeingWaitedForOnceItsHoursRunOut()
    {
        HeldClock clock = HeldClock.Broadcasting(2026, 8, 19, 2, 59, 0);
        var progress = new ScheduleProgress(clock);

        Gather(progress, FirstBasic, FirstBasic, segments: 4, lastSection: 31, from: 1);

        Assert.Equal([0], progress.SegmentsAwaited(Service, FirstBasic));

        clock.MoveOn(TimeSpan.FromMinutes(2));

        Assert.Empty(progress.SegmentsAwaited(Service, FirstBasic));
        Assert.True(progress.IsWhole(Service, FirstBasic));
    }

    [Fact]
    public void AScheduleWhoseSpentHoursNeverArriveStillReachesTheEnd()
    {
        var progress = new ScheduleProgress(HeldClock.Broadcasting(2026, 8, 19, 15, 20, 0));

        Gather(progress, FirstBasic, FirstBasic, segments: 32, lastSection: 255, from: 5);
        Gather(progress, FirstExtended, FirstExtended, segments: 32, lastSection: 255, from: 5);

        Assert.Equal(ScheduleCompleteness.Complete, progress.Completeness);
    }

    private static HeldClock Midnight() => HeldClock.Broadcasting(2026, 8, 19, 0, 0, 0);

    private static void Gather(
        ScheduleProgress progress,
        int tableId,
        int lastTableId,
        int segments,
        int lastSection,
        int version = 0,
        int service = SomeService,
        int from = 0)
    {
        for (int segment = from; segment < segments; segment++)
        {
            int section = segment * ScheduleProgress.SectionsPerSegment;

            progress.Saw(Table(tableId, lastTableId, section, section, lastSection, version, service));
        }
    }

    private static EventInformationTable Table(
        int tableId,
        int lastTableId,
        int section,
        int segmentLast,
        int lastSection,
        int version = 0,
        int service = SomeService)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = service,
                VersionNumber = version,
                SectionNumber = section,
                LastSectionNumber = lastSection,
                Body =
                [
                    (byte)(SomeStream >> 8), (byte)(SomeStream & 0xFF),
                    (byte)(SomeNetwork >> 8), (byte)(SomeNetwork & 0xFF),
                    (byte)segmentLast,
                    (byte)lastTableId,
                ],
            }))).Table;
}
