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

        Assert.Equal(new CatalogHeard(CatalogSeries.Lead + TimeSpan.FromSeconds(900), CatalogSeries.Lead + TimeSpan.FromSeconds(929), SegmentKind.Break, 2, 1, 1, 0.5), heard);
        Assert.Equal(new CatalogHeard(CatalogSeries.Lead + TimeSpan.FromSeconds(300), CatalogSeries.Lead + TimeSpan.FromSeconds(329), SegmentKind.Break, 3, 2, 2, 0.9), grown);
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

    [Fact(DisplayName = "the answer given on the recording itself is set aside, and an answer on an earlier recording still decides the entry")]
    public void TheRecordingsOwnAnswerIsSetAside()
    {
        CatalogEntryId sound = CatalogEntryId.New();
        RecordingId first = RecordingId.New();
        RecordingId judged = RecordingId.New();
        PlacedOccurrence[] twiceInTheJudged =
        [
            CatalogSeries.At(sound, Story.Episode(1), 900, 29, SegmentKind.Programme, judged),
            CatalogSeries.At(sound, Story.Episode(1), 1500, 29, SegmentKind.Programme, judged),
        ];

        LearningCatalog unanswered = new([new CatalogSound(sound, TimeSpan.FromSeconds(29), [CatalogSeries.At(sound, Story.Episode(0), 300, 29, recording: first), .. twiceInTheJudged])], SeriesLinks.None);
        LearningCatalog answeredEarlier = new([new CatalogSound(sound, TimeSpan.FromSeconds(29), [CatalogSeries.At(sound, Story.Episode(0), 300, 29, SegmentKind.Break, first), .. twiceInTheJudged])], SeriesLinks.None);

        Assert.All(unanswered.HeardBy(judged, Story.Episode(1).RecordingStartedAt), heard => Assert.Equal((SegmentKind.Break, 0.5), (heard.Kind!.Value, heard.Confidence!.Value)));
        Assert.All(answeredEarlier.HeardBy(judged, Story.Episode(1).RecordingStartedAt), heard => Assert.Equal((SegmentKind.Break, 1.0), (heard.Kind!.Value, heard.Confidence!.Value)));
        Assert.Equal(2, answeredEarlier.HeardBy(judged, Story.Episode(1).RecordingStartedAt).Count);
    }

    [Fact(DisplayName = "the answer of a recording held out is set aside too, so an entry it decided falls back to what its stretches say")]
    public void TheAnswerOfARecordingHeldOutIsSetAside()
    {
        CatalogEntryId advert = CatalogEntryId.New();
        RecordingId first = RecordingId.New();
        RecordingId judged = RecordingId.New();
        LearningCatalog catalog = new(
        [
            new CatalogSound(advert, TimeSpan.FromSeconds(29), [
                CatalogSeries.At(advert, Story.Episode(0), 300, 29, SegmentKind.Break, first),
                CatalogSeries.At(advert, Story.Episode(1), 900, 29, recording: judged),
            ]),
        ],
        SeriesLinks.None);
        DateTime startedAt = Story.Episode(1).RecordingStartedAt;

        Assert.Equal(1.0, Assert.Single(catalog.HeardBy(judged, startedAt)).Confidence);
        Assert.Equal(0.5, Assert.Single(catalog.HeardBy(judged, startedAt, first)).Confidence);
        Assert.True(catalog.HearsTheAnswerOf(judged, startedAt, first));
        Assert.False(catalog.HearsTheAnswerOf(first, Story.Episode(0).RecordingStartedAt, judged));
        Assert.False(catalog.HearsTheAnswerOf(judged, startedAt, judged));
        Assert.False(catalog.HearsTheAnswerOf(judged, Story.Episode(0).RecordingStartedAt.AddTicks(-1), first));
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
