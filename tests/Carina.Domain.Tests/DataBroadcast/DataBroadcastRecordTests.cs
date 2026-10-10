using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastRecordTests
{
    private static readonly DataBroadcastRecord Record = new(
        1_000,
        Carousels.Entry,
        [
            new RecordedCarousel(Carousels.Other, 2, [Carousels.Version(Carousels.Other, 0, 1, 50)]),
            new RecordedCarousel(
                Carousels.Entry,
                1,
                [
                    Carousels.Version(Carousels.Entry, 1, 4, 300),
                    Carousels.Version(Carousels.Entry, 0, 1, 100),
                    Carousels.Version(Carousels.Entry, 0, 2, 200),
                ]),
        ],
        [Carousels.Event(2, 250), Carousels.Event(1, 150), Carousels.Event(3, 400)],
        false);

    [Theory(DisplayName = "BR-BD-006: at each moment every module is played at the version first seen latest by then")]
    [InlineData(0L, "")]
    [InlineData(99L, "50/0/1")]
    [InlineData(100L, "40/0/1 50/0/1")]
    [InlineData(199L, "40/0/1 50/0/1")]
    [InlineData(200L, "40/0/2 50/0/1")]
    [InlineData(300L, "40/0/2 40/1/4 50/0/1")]
    [InlineData(long.MaxValue, "40/0/2 40/1/4 50/0/1")]
    public void EachModuleIsPlayedAtTheVersionFirstSeenLatestByThen(long at, string expected)
    {
        Assert.Equal(
            expected,
            string.Join(' ', Record.VersionsAt(at).Select(version => $"{version.Tag:x2}/{version.ModuleId}/{version.Version}")));
    }

    [Fact(DisplayName = "BR-BD-006: a version first seen after the moment is never used, whatever its number")]
    public void AVersionFirstSeenLaterIsNotUsedWhateverItsNumber()
    {
        DataBroadcastRecord wrapped = new(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, 255, 100), Carousels.Version(Carousels.Entry, 0, 0, 200)])],
            [],
            false);

        Assert.Equal(255, Assert.Single(wrapped.VersionsAt(150)).Version);
        Assert.Equal(0, Assert.Single(wrapped.VersionsAt(250)).Version);
    }

    [Fact(DisplayName = "BR-BD-006: the event messages between two moments are the ones from the first up to the second, in the order they fire")]
    public void TheEventsBetweenTwoMomentsAreTheOnesFromTheFirstUpToTheSecond()
    {
        Assert.Equal([1, 2], Record.EventsBetween(150, 400).Select(message => message.Id));
        Assert.Empty(Record.EventsBetween(151, 250));
        Assert.Equal([2, 3], Record.EventsBetween(250, 401).Select(message => message.Id));
    }

    [Fact(DisplayName = "BR-BD-006: going back over moments already played gives their event messages again")]
    public void GoingBackGivesTheEventsAgain()
    {
        Assert.Equal([1, 2, 3], Record.EventsBetween(0, 1_000).Select(message => message.Id));
        Assert.Equal([2, 3], Record.EventsBetween(200, 1_000).Select(message => message.Id));
    }

    [Fact]
    public void TheRecordHoldsItsCarouselsInOrderOfTagAndTheirVersionsInOrderFirstSeen()
    {
        Assert.Equal([Carousels.Entry, Carousels.Other], Record.Carousels.Select(carousel => carousel.Tag));
        Assert.Equal([100L, 200L, 300L], Record.Carousels[0].Versions.Select(version => version.FirstSeen));
        Assert.Equal(3, Record.Modules);
    }

    [Fact(DisplayName = "BR-BD-005: the same tag, module and version is held once")]
    public void ACarouselRefusesTheSameVersionTwice()
    {
        Assert.Throws<ArgumentException>(() => new RecordedCarousel(
            Carousels.Entry,
            1,
            [Carousels.Version(Carousels.Entry, 0, 1, 100), Carousels.Version(Carousels.Entry, 0, 1, 200)]));
    }

    [Fact]
    public void ACarouselRefusesAVersionOfAnotherCarousel()
    {
        Assert.Throws<ArgumentException>(() => new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Other, 0, 1, 100)]));
    }

    [Fact]
    public void ARecordRefusesTheSameCarouselTwice()
    {
        Assert.Throws<ArgumentException>(() => new DataBroadcastRecord(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 1, []), new RecordedCarousel(Carousels.Entry, 1, [])],
            [],
            false));
    }

    [Fact(DisplayName = "BR-BD-005: a record within its size is kept whole")]
    public void ARecordWithinItsSizeIsKeptWhole()
    {
        Assert.Same(Record, Record.Within(Record.Bytes));
        Assert.False(Record.Within(DataBroadcastRecord.MostBytes).Incomplete);
    }

    [Fact(DisplayName = "BR-BD-005: a record over its size leaves out the superseded versions first seen earliest and is marked incomplete")]
    public void ARecordOverItsSizeLeavesOutTheOldestSupersededVersions()
    {
        DataBroadcastRecord record = Superseding();
        long each = Carousels.Version(Carousels.Entry, 1, 1, 0).Bytes;

        DataBroadcastRecord kept = record.Within(record.Bytes - each);

        Assert.True(kept.Incomplete);
        Assert.Equal(["40/0/1", "40/1/2", "40/1/3"], Held(kept));
        Assert.True(kept.Bytes <= record.Bytes - each);
        Assert.Equal((record.StartsAt, record.EntryTag), (kept.StartsAt, kept.EntryTag));
        Assert.Equal(record.Events, kept.Events);
    }

    [Fact(DisplayName = "BR-BD-005: the startup document and the latest version of every module stay even when the record does not fit")]
    public void TheStartupDocumentAndTheLatestVersionsStay()
    {
        DataBroadcastRecord kept = Superseding().Within(0);

        Assert.True(kept.Incomplete);
        Assert.Equal(["40/0/1", "40/1/3"], Held(kept));
    }

    [Fact(DisplayName = "BR-BD-005: every version of the startup document stays")]
    public void EveryVersionOfTheStartupDocumentStays()
    {
        DataBroadcastRecord record = new(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, 1, 100), Carousels.Version(Carousels.Entry, 0, 2, 200)])],
            [],
            false);

        Assert.Equal(["40/0/1", "40/0/2"], Held(record.Within(0)));
    }

    [Fact(DisplayName = "BR-BD-005: event messages count toward the size of a record")]
    public void EventMessagesCountTowardTheSize()
    {
        DataBroadcastRecord quiet = Superseding();
        EventMessage loud = new(1, 9, 1, EventTiming.Npt, 500, new byte[1_000]);
        DataBroadcastRecord record = new(quiet.StartsAt, quiet.EntryTag, quiet.Carousels, [loud], false);

        Assert.Equal(quiet.Bytes + loud.Bytes, record.Bytes);
        Assert.Equal(EventMessage.FramingBytes + 1_000, loud.Bytes);
        Assert.False(quiet.Within(quiet.Bytes).Incomplete);
        Assert.Equal(["40/0/1", "40/1/3"], Held(record.Within(quiet.Bytes)));
    }

    [Fact(DisplayName = "BR-BD-005: a record is measured by its header, its carousels with their versions, and its event messages")]
    public void ARecordIsMeasuredByItsHeaderCarouselsVersionsAndEvents()
    {
        DataBroadcastRecord empty = new(0, Carousels.Entry, [], [], false);
        RecordedCarousel carousel = new(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, 1, 0)]);

        Assert.Equal(DataBroadcastRecord.HeaderBytes + DataBroadcastRecord.EventCountBytes, empty.Bytes);
        Assert.Equal(22 + 4, empty.Bytes);
        Assert.Equal(7 + carousel.Versions[0].Bytes, carousel.Bytes);
    }

    [Fact]
    public void ASizeBelowNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Record.Within(-1));
    }

    [Fact]
    public void TheMostARecordTakesIsAQuarterOfAGibibyte()
    {
        Assert.Equal(256L * 1024 * 1024, DataBroadcastRecord.MostBytes);
    }

    [Fact]
    public void AVersionIsMeasuredByThePathFormLengthAndBodyOfEachResource()
    {
        ModuleVersion version = new(
            Carousels.Entry,
            0,
            1,
            0,
            0,
            [Carousels.Resource("startup.bml", 100), Carousels.Resource("a.png", 20)]);

        Assert.Equal(23 + (7 + 11 + 100) + (7 + 5 + 20), version.Bytes);
    }

    [Fact(DisplayName = "BR-BD-006: of two versions first seen at the same moment, the one that arrived last is played")]
    public void OfTwoVersionsFirstSeenTogetherTheOneThatArrivedLastIsPlayed()
    {
        Assert.Equal(2, Assert.Single(Together(1, 2).VersionsAt(100)).Version);
        Assert.Equal(1, Assert.Single(Together(2, 1).VersionsAt(100)).Version);
    }

    private static DataBroadcastRecord Together(int first, int second)
        => new(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 1, [Carousels.Version(Carousels.Entry, 0, first, 100), Carousels.Version(Carousels.Entry, 0, second, 100)])],
            [],
            false);

    [Fact]
    public void ARecordHoldsTwoDownloadsOfOneCarouselInTheOrderTheyAreGiven()
    {
        DataBroadcastRecord record = new(
            0,
            Carousels.Entry,
            [new RecordedCarousel(Carousels.Entry, 9, []), new RecordedCarousel(Carousels.Entry, 2, [])],
            [],
            false);

        Assert.Equal([9u, 2u], record.Carousels.Select(carousel => carousel.DownloadId));
    }

    private static DataBroadcastRecord Superseding()
        => new(
            0,
            Carousels.Entry,
            [
                new RecordedCarousel(
                    Carousels.Entry,
                    1,
                    [
                        Carousels.Version(Carousels.Entry, 0, 1, 50),
                        Carousels.Version(Carousels.Entry, 1, 1, 100),
                        Carousels.Version(Carousels.Entry, 1, 2, 200),
                        Carousels.Version(Carousels.Entry, 1, 3, 300),
                    ]),
            ],
            [],
            false);

    private static IReadOnlyList<string> Held(DataBroadcastRecord record)
        => [.. record.Carousels.SelectMany(carousel => carousel.Versions).Select(version => $"{version.Tag:x2}/{version.ModuleId}/{version.Version}")];
}
