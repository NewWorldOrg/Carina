using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastRecordBuilderTests
{
    [Fact(DisplayName = "BR-BS-002: the record keeps the versions the catalog no longer holds valid")]
    public void TheRecordKeepsVersionsNoLongerValid()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 2)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 2), 300));

        RecordedCarousel carousel = Assert.Single(record.Carousels);
        Assert.Equal([(1, 100L), (2, 300L)], carousel.Versions.Select(version => (version.Version, version.FirstSeen)));
        Assert.Equal(Carousels.Entry, record.EntryTag);
        Assert.False(record.Incomplete);
    }

    [Fact(DisplayName = "BR-BD-005: a version is last seen when a listing last held it valid")]
    public void AVersionIsLastSeenWhenAListingLastHeldItValid()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [1], (0, 1), (1, 2)), 500),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 2), (1, 2)), 900));

        ModuleVersion version = Assert.Single(Assert.Single(record.Carousels).Versions);
        Assert.Equal((100L, 500L), (version.FirstSeen, version.LastSeen));
    }

    [Fact(DisplayName = "BR-BD-005: the same tag, module and version is held once")]
    public void TheSameVersionPutTogetherAgainIsHeldOnce()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1), 300));

        ModuleVersion version = Assert.Single(Assert.Single(record.Carousels).Versions);
        Assert.Equal(100L, version.FirstSeen);
    }

    [Fact(DisplayName = "BR-BD-005: the record holds every event message and the download of every carousel")]
    public void TheRecordHoldsEveryEventAndEveryDownload()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, 7, [], (0, 1)), 0),
            (Carousels.Listing(Carousels.Other, 9, [], (0, 1)), 0),
            (new CarouselSignal.EventTimed(Carousels.Event(2, 600)), 500),
            (new CarouselSignal.EventTimed(Carousels.Event(1, 400)), 300));

        Assert.Equal([(Carousels.Entry, 7u), (Carousels.Other, 9u)], record.Carousels.Select(carousel => (carousel.Tag, carousel.DownloadId)));
        Assert.Equal([1, 2], record.Events.Select(message => message.Id));
        Assert.Equal(0, record.Modules);
    }

    [Fact(DisplayName = "BR-BD-005: a version put together again with other content takes the place of what was held")]
    public void AVersionPutTogetherAgainWithOtherContentTakesItsPlace()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, (0, 1), (1, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Completed(Carousels.Entry, 1, 1), 150),
            (Carousels.Listing(Carousels.Entry, 1, [0], (0, 1), (1, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1, "other.bml"), 300));

        Assert.Equal(
            [(1, 150L, "startup.bml"), (0, 300L, "other.bml")],
            Assert.Single(record.Carousels).Versions.Select(version => (version.ModuleId, version.FirstSeen, version.Resources[0].Path)));
    }

    [Fact(DisplayName = "BR-BD-005: a carousel whose download changes goes on as another carousel of the record")]
    public void ACarouselWhoseDownloadChangesGoesOnAsAnotherCarousel()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Carried(), 0),
            (Carousels.Listing(Carousels.Entry, 1, [], (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Listing(Carousels.Entry, 2, [], (0, 1)), 200),
            (Carousels.Completed(Carousels.Entry, 0, 1), 300));

        Assert.Equal(
            [(Carousels.Entry, 1u, 100L), (Carousels.Entry, 2u, 300L)],
            record.Carousels.Select(carousel => (carousel.Tag, carousel.DownloadId, Assert.Single(carousel.Versions).FirstSeen)));
    }

    [Fact]
    public void AModuleThatArrivesBeforeTheProgrammeMapIsReadKeepsTheDownloadItCameIn()
    {
        DataBroadcastRecord record = Read(
            (Carousels.Listing(Carousels.Entry, 7, [], (0, 1)), 0),
            (Carousels.Completed(Carousels.Entry, 0, 1), 100),
            (Carousels.Carried(), 200));

        Assert.Equal(7u, Assert.Single(record.Carousels).DownloadId);
    }

    [Fact]
    public void NothingIsRecordedWhenNoCatalogWasEverRead()
    {
        DataBroadcastRecordBuilder builder = new();
        CarouselState state = new();

        foreach (CarouselDelta delta in state.Apply(new CarouselSignal.NotCarried(), 0))
        {
            builder.Take(delta, 0);
        }

        Assert.Null(builder.Build(0));
    }

    [Fact]
    public void TheRecordBeginsWhereItIsToldTo()
    {
        Assert.Equal(12_345L, ReadFrom(12_345, (Carousels.Carried(), 0)).StartsAt);
    }

    private static DataBroadcastRecord Read(params (CarouselSignal Signal, long At)[] signals)
        => ReadFrom(0, signals);

    private static DataBroadcastRecord ReadFrom(long startsAt, params (CarouselSignal Signal, long At)[] signals)
    {
        CarouselState state = new();
        DataBroadcastRecordBuilder builder = new();

        foreach ((CarouselSignal signal, long at) in signals)
        {
            foreach (CarouselDelta delta in state.Apply(signal, at))
            {
                builder.Take(delta, at);
            }
        }

        return builder.Build(startsAt) ?? throw new InvalidOperationException("A catalog was read.");
    }
}
