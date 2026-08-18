using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;

namespace Carina.Broadcast.Tests.Tables;

public sealed class ScheduleProgressTests
{
    private const int SomeService = 1024;

    private const int FirstBasic = EventInformationTable.FirstScheduleActualTableId;

    private const int LastBasic = FirstBasic + 1;

    private const int FirstExtended = FirstBasic + 8;

    private const int LastExtended = FirstExtended + 1;

    [Fact]
    public void ATableIsNotWholeUntilEverySegmentHasBeenSeen()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 3, lastSection: 31);

        Assert.False(progress.IsWhole(FirstBasic));
        Assert.Equal([3], progress.SegmentsAwaited(FirstBasic));
    }

    [Fact]
    public void ATableWhoseEverySegmentArrivedAwaitsNothing()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, FirstBasic, segments: 4, lastSection: 31);

        Assert.Empty(progress.SegmentsAwaited(FirstBasic));
        Assert.True(progress.IsWhole(FirstBasic));
    }

    [Fact]
    public void ASegmentCarryingSeveralSectionsIsNotWholeUntilAllOfThemArrive()
    {
        var progress = new ScheduleProgress();

        progress.Saw(Table(FirstBasic, FirstBasic, section: 0, segmentLast: 2, lastSection: 7));

        Assert.Equal([0], progress.SegmentsAwaited(FirstBasic));

        progress.Saw(Table(FirstBasic, FirstBasic, section: 1, segmentLast: 2, lastSection: 7));
        progress.Saw(Table(FirstBasic, FirstBasic, section: 2, segmentLast: 2, lastSection: 7));

        Assert.Empty(progress.SegmentsAwaited(FirstBasic));
    }

    [Fact]
    public void TheBasicTablesBeingWholeIsEnoughToLetGoOfTheTuner()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.BasicOnly, progress.Completeness);
    }

    [Fact]
    public void OnlyOneOfTheBasicTablesIsNotEnough()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void TheDetailedTablesOnTopOfTheBasicOnesMeanThereIsNothingLeftToWaitFor()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, FirstExtended, LastExtended, segments: 4, lastSection: 31);
        Gather(progress, LastExtended, LastExtended, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Complete, progress.Completeness);
    }

    [Fact]
    public void TheDetailedTablesAloneDoNotMakeTheScheduleComplete()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstExtended, LastExtended, segments: 4, lastSection: 31);
        Gather(progress, LastExtended, LastExtended, segments: 4, lastSection: 31);

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void EachTableKeepsItsOwnVersionSoOneMovingOnDoesNotUndoTheOthers()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31, version: 9);

        Assert.Empty(progress.SegmentsAwaited(FirstBasic));
        Assert.Empty(progress.SegmentsAwaited(LastBasic));
        Assert.Equal(ScheduleCompleteness.BasicOnly, progress.Completeness);
    }

    [Fact]
    public void ATableThatMovesToANewVersionStartsOverOnItsOwn()
    {
        var progress = new ScheduleProgress();

        Gather(progress, FirstBasic, LastBasic, segments: 4, lastSection: 31);
        Gather(progress, LastBasic, LastBasic, segments: 4, lastSection: 31);

        progress.Saw(Table(FirstBasic, LastBasic, section: 0, segmentLast: 0, lastSection: 31, version: 9));

        Assert.Equal([1, 2, 3], progress.SegmentsAwaited(FirstBasic));
        Assert.Empty(progress.SegmentsAwaited(LastBasic));
        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
    }

    [Fact]
    public void WhatIsOnNowAndNextIsNotPartOfTheSchedule()
    {
        var progress = new ScheduleProgress();

        progress.Saw(Table(
            EventInformationTable.PresentFollowingActualTableId,
            EventInformationTable.PresentFollowingActualTableId,
            section: 0,
            segmentLast: 0,
            lastSection: 1));

        Assert.Equal(ScheduleCompleteness.Incomplete, progress.Completeness);
        Assert.Empty(progress.SegmentsAwaited(EventInformationTable.PresentFollowingActualTableId));
    }

    [Fact]
    public void ATableNeverSeenAwaitsNothingBecauseNothingIsKnownOfIt()
    {
        Assert.Empty(new ScheduleProgress().SegmentsAwaited(FirstBasic));
        Assert.False(new ScheduleProgress().IsWhole(FirstBasic));
    }

    private static void Gather(
        ScheduleProgress progress,
        int tableId,
        int lastTableId,
        int segments,
        int lastSection,
        int version = 0)
    {
        for (var segment = 0; segment < segments; segment++)
        {
            var section = segment * ScheduleProgress.SectionsPerSegment;

            progress.Saw(Table(tableId, lastTableId, section, section, lastSection, version));
        }
    }

    private static EventInformationTable Table(
        int tableId,
        int lastTableId,
        int section,
        int segmentLast,
        int lastSection,
        int version = 0)
        => Assert.IsType<TableRead<EventInformationTable>.Parsed>(
            EventInformationTable.Read(CarriedSection.Of(new SectionWriter
            {
                TableId = tableId,
                TableIdExtension = SomeService,
                VersionNumber = version,
                SectionNumber = section,
                LastSectionNumber = lastSection,
                Body = [0x7F, 0xE3, 0x7F, 0xE3, (byte)segmentLast, (byte)lastTableId],
            }))).Table;
}
