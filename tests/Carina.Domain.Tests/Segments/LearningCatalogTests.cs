using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class LearningCatalogTests
{
    private static readonly CatalogSeries Story = new(1, "架空の物語", new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

    private static readonly CatalogSeries Elsewhere = new(2, "別の架空の話", new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc));

    [Fact(DisplayName = "a recording hears an entry as the stretches of recordings started no later than it estimate it, not as later recordings grew it")]
    public void ARecordingHearsTheEntryAsItStoodThen()
    {
        CatalogEntryId advert = CatalogEntryId.New();
        RecordingId first = RecordingId.New();
        RecordingId judged = RecordingId.New();
        RecordingId later = RecordingId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(advert, TimeSpan.FromSeconds(29), [
                CatalogSeries.At(advert, Story.Episode(0), 600, 29, recording: first),
                CatalogSeries.At(advert, Story.Episode(1), 900, 29, recording: judged),
                CatalogSeries.At(advert, Elsewhere.Episode(2), 300, 29, recording: later),
            ]),
        ],
        SeriesLinks.None);

        CatalogHeard heard = Assert.Single(catalog.HeardBy(judged, Story.Episode(1).RecordingStartedAt));
        CatalogHeard grown = Assert.Single(catalog.HeardBy(later, Elsewhere.Episode(2).RecordingStartedAt));

        Assert.Equal(new CatalogHeard(CatalogSeries.Lead + TimeSpan.FromSeconds(900), CatalogSeries.Lead + TimeSpan.FromSeconds(929), SegmentKind.Break, 2, 1, 1, 0.5, advert), heard);
        Assert.Equal(new CatalogHeard(CatalogSeries.Lead + TimeSpan.FromSeconds(300), CatalogSeries.Lead + TimeSpan.FromSeconds(329), SegmentKind.Break, 3, 2, 2, 0.9, advert), grown);
    }

    [Fact(DisplayName = "an entry heard only once by the time a recording started is not heard by it at all")]
    public void AnEntryNotYetStartedIsNotHeard()
    {
        CatalogEntryId advert = CatalogEntryId.New();
        RecordingId judged = RecordingId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(advert, TimeSpan.FromSeconds(29), [
                CatalogSeries.At(advert, Story.Episode(1), 610, 29, recording: judged),
                CatalogSeries.At(advert, Elsewhere.Episode(2), 300, 29),
            ]),
        ],
        SeriesLinks.None);

        Assert.Empty(catalog.HeardBy(judged, Story.Episode(1).RecordingStartedAt));
        Assert.Empty(catalog.HeardBy(RecordingId.New(), Story.Episode(1).RecordingStartedAt));
    }

    [Fact(DisplayName = "the catalog as it stands now hears a recording with every stretch it has counted, later ones included")]
    public void TheCatalogNowCountsEveryStretch()
    {
        CatalogEntryId advert = CatalogEntryId.New();
        RecordingId judged = RecordingId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(advert, TimeSpan.FromSeconds(29), [
                CatalogSeries.At(advert, Story.Episode(1), 900, 29, recording: judged),
                CatalogSeries.At(advert, Story.Episode(2), 600, 29),
                CatalogSeries.At(advert, Elsewhere.Episode(2), 300, 29),
            ]),
        ],
        SeriesLinks.None);

        Assert.Empty(catalog.HeardBy(judged, Story.Episode(1).RecordingStartedAt));
        Assert.Equal(
            new CatalogHeard(CatalogSeries.Lead + TimeSpan.FromSeconds(900), CatalogSeries.Lead + TimeSpan.FromSeconds(929), SegmentKind.Break, 3, 2, 2, 0.9, advert),
            Assert.Single(catalog.HeardNow(judged)));
    }

    [Fact(DisplayName = "BR-CD-008: the entries set aside when a recording is judged are every entry heard in it, however often, and every entry first heard after it started")]
    public void TheEntriesSetAsideAreItsOwnAndThoseStartedLater()
    {
        RecordingId judged = RecordingId.New();
        CatalogEntryId heardInIt = CatalogEntryId.New();
        CatalogEntryId onceInIt = CatalogEntryId.New();
        CatalogEntryId startedLater = CatalogEntryId.New();
        CatalogEntryId startedEarlier = CatalogEntryId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(heardInIt, TimeSpan.FromSeconds(29), [CatalogSeries.At(heardInIt, Story.Episode(0), 600, 29), CatalogSeries.At(heardInIt, Story.Episode(1), 600, 29, recording: judged)]),
            new CatalogSound(onceInIt, TimeSpan.FromSeconds(29), [CatalogSeries.At(onceInIt, Story.Episode(1), 900, 29, recording: judged), CatalogSeries.At(onceInIt, Elsewhere.Episode(3), 900, 29)]),
            new CatalogSound(startedLater, TimeSpan.FromSeconds(29), [CatalogSeries.At(startedLater, Story.Episode(2), 600, 29), CatalogSeries.At(startedLater, Elsewhere.Episode(2), 600, 29)]),
            new CatalogSound(startedEarlier, TimeSpan.FromSeconds(29), [CatalogSeries.At(startedEarlier, Story.Episode(0), 300, 29), CatalogSeries.At(startedEarlier, Elsewhere.Episode(3), 300, 29)]),
        ],
        SeriesLinks.None);

        IReadOnlySet<CatalogEntryId> setAside = catalog.SetAsideFor(judged, Story.Episode(1).RecordingStartedAt);

        Assert.True(setAside.SetEquals([heardInIt, onceInIt, startedLater]));
        Assert.Equal([heardInIt], catalog.HeardBy(judged, Story.Episode(1).RecordingStartedAt).Select(heard => heard.Entry));
    }

    [Fact(DisplayName = "an empty catalog hears nothing, and a context told what it hears keeps everything else")]
    public void AContextHearsWhatItIsTold()
    {
        SegmentContext context = new(Story.Episode(0).NetworkId, Story.Episode(0).ServiceId, "架空の物語", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(31), [], [], CornerLogo.None, []);
        CatalogHeard heard = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25), SegmentKind.Break, 3, 2, 2, 0.9);

        SegmentContext hearing = context.Hearing([heard]);

        Assert.Empty(LearningCatalog.Empty.HeardBy(RecordingId.New(), Story.Episode(0).RecordingStartedAt));
        Assert.Equal([heard], hearing.Catalog);
        Assert.Equal((context.Station, context.Series, context.ProgrammeStarts, context.ProgrammeEnds), (hearing.Station, hearing.Series, hearing.ProgrammeStarts, hearing.ProgrammeEnds));
    }
}
