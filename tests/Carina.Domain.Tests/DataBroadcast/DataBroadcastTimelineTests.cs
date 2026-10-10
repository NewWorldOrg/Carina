using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastTimelineTests
{
    private const long Second = StreamClock.Hertz;

    private static readonly TimeSpan Begins = TimeSpan.FromSeconds(100);

    private static readonly DataBroadcastRecord Record = new(
        100 * Second,
        Carousels.Entry,
        [
            new RecordedCarousel(Carousels.Other, 9, [Seen(Carousels.Other, 0, 1, 90, 400)]),
            new RecordedCarousel(
                Carousels.Entry,
                1,
                [
                    Seen(Carousels.Entry, 0, 1, 95, 99),
                    Seen(Carousels.Entry, 0, 2, 99, 130),
                    Seen(Carousels.Entry, 0, 3, 130, 400),
                    Seen(Carousels.Entry, 1, 1, 105, 400),
                    Seen(Carousels.Entry, 2, 1, 395, 400),
                ]),
        ],
        [Carousels.Event(1, 98 * Second), Carousels.Event(2, 110 * Second), Carousels.Event(3, 390 * Second)],
        true,
        autoStart: true);

    [Fact(DisplayName = "BR-BD-006: on the recording itself every version runs from when it was first seen to when it was last seen, less where the file's clock begins")]
    public void OnTheRecordingEveryVersionRunsFromFirstToLastSeenLessWhereTheClockBegins()
    {
        DataBroadcastTimeline timeline = DataBroadcastTimeline.Of(Record.Outline, Begins, null, TimeSpan.Zero);

        Assert.Equal(
            ["40/1/0/2 0-30", "40/1/1/1 5-300", "40/1/0/3 30-300", "40/1/2/1 295-300"],
            Described(timeline.Carousels.Single(carousel => carousel.Tag == Carousels.Entry)));
        Assert.Equal(["50/9/0/1 0-300"], Described(timeline.Carousels.Single(carousel => carousel.Tag == Carousels.Other)));
        Assert.Equal([10.0, 290.0], timeline.Events.Select(placed => placed.At.TotalSeconds));
        Assert.Equal([2, 3], timeline.Events.Select(placed => placed.Message.Id));
    }

    [Fact(DisplayName = "BR-BD-006: the timeline carries where the data broadcast is entered, whether it opens by itself, its startup document and whether it is incomplete")]
    public void TheTimelineCarriesTheEntryAndWhetherItOpensByItself()
    {
        DataBroadcastTimeline timeline = DataBroadcastTimeline.Of(Record.Outline, Begins, null, TimeSpan.Zero);

        Assert.Equal(
            (Carousels.Entry, true, "/40/0000/startup.bml", true),
            (timeline.EntryTag, timeline.AutoStart, timeline.StartupDocument, timeline.Incomplete));
        Assert.Equal([Carousels.Entry, Carousels.Other], timeline.Carousels.Select(carousel => carousel.Tag));
    }

    [Fact(DisplayName = "BR-BD-006: on an artefact the versions and events are moved by the job's shift, and none first seen or firing after its end is placed")]
    public void OnAnArtefactNothingAfterItsEndIsPlaced()
    {
        DataBroadcastTimeline timeline = DataBroadcastTimeline.Of(Record.Outline, TimeSpan.FromSeconds(102), TimeSpan.FromSeconds(200), TimeSpan.Zero);

        Assert.Equal(
            ["40/1/0/2 0-28", "40/1/1/1 3-200", "40/1/0/3 28-200"],
            Described(timeline.Carousels.Single(carousel => carousel.Tag == Carousels.Entry)));
        Assert.Equal([8.0], timeline.Events.Select(placed => placed.At.TotalSeconds));
    }

    [Fact(DisplayName = "BR-BD-006: from a later second only the versions still running there and the events firing there or later are placed")]
    public void FromALaterSecondOnlyWhatRunsThereOrLaterIsPlaced()
    {
        DataBroadcastTimeline timeline = DataBroadcastTimeline.Of(Record.Outline, Begins, null, TimeSpan.FromSeconds(30));

        Assert.Equal(
            ["40/1/0/2 0-30", "40/1/1/1 5-300", "40/1/0/3 30-300", "40/1/2/1 295-300"],
            Described(timeline.Carousels.Single(carousel => carousel.Tag == Carousels.Entry)));
        Assert.Equal([290.0], timeline.Events.Select(placed => placed.At.TotalSeconds));

        DataBroadcastTimeline later = DataBroadcastTimeline.Of(Record.Outline, Begins, null, TimeSpan.FromSeconds(31));

        Assert.Equal(
            ["40/1/1/1 5-300", "40/1/0/3 30-300", "40/1/2/1 295-300"],
            Described(later.Carousels.Single(carousel => carousel.Tag == Carousels.Entry)));
    }

    [Fact(DisplayName = "BR-BD-006: a version running past the source's end ends there, so asking from after that end leaves it out")]
    public void AVersionRunningPastTheEndEndsThereAndIsLeftOutFromAfterIt()
    {
        DataBroadcastRecord record = new(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 1, [Seen(Carousels.Entry, 0, 1, 10, 150)])],
            [],
            false);

        DataBroadcastTimeline before = DataBroadcastTimeline.Of(record.Outline, TimeSpan.Zero, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100));
        DataBroadcastTimeline after = DataBroadcastTimeline.Of(record.Outline, TimeSpan.Zero, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(120));

        Assert.Equal(["40/1/0/1 10-100"], Described(Assert.Single(before.Carousels)));
        Assert.Empty(Assert.Single(after.Carousels).Versions);
    }

    [Fact]
    public void ATimelineStartsAtOrAfterTheSourcesZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DataBroadcastTimeline.Of(Record.Outline, Begins, null, TimeSpan.FromTicks(-1)));
    }

    private static ModuleVersion Seen(int tag, int moduleId, int version, long firstSecond, long lastSecond)
        => new(tag, moduleId, version, firstSecond * Second, lastSecond * Second, [Carousels.Resource("a.bml")]);

    private static string[] Described(PlacedCarousel carousel)
        => [.. carousel.Versions.Select(placed =>
            $"{carousel.Tag:x2}/{carousel.DownloadId}/{placed.Module.ModuleId}/{placed.Module.Version} {placed.From.TotalSeconds:0.###}-{placed.To.TotalSeconds:0.###}")];
}
