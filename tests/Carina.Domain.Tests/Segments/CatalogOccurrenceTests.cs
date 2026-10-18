using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class CatalogOccurrenceTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly SoundSpan Stretch = new(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615));

    [Fact(DisplayName = "a stretch found copies the station, series and recording start from the copy of its programme")]
    public void AStretchCopiesItsProgramme()
    {
        CatalogEntryId entry = CatalogEntryId.New();
        RecordingId recording = RecordingId.New();
        ProgrammeCopy programme = Copy("架空の物語 #3", "架空の物語");

        CatalogOccurrence occurrence = CatalogOccurrence.Found(entry, recording, Stretch, programme, 0.12, Evening.AddDays(1));

        Assert.Equal(0, occurrence.Id);
        Assert.Equal(entry, occurrence.EntryId);
        Assert.Equal(recording, occurrence.RecordingId);
        Assert.Equal(Stretch.Starts, occurrence.StartsAt);
        Assert.Equal(Stretch.Ends, occurrence.EndsAt);
        Assert.Equal(new NetworkId(1), occurrence.NetworkId);
        Assert.Equal(new ServiceId(2), occurrence.ServiceId);
        Assert.Equal("架空の物語", occurrence.SeriesName);
        Assert.Equal(Evening.AddMinutes(-1), occurrence.RecordingStartedAt);
        Assert.Equal(0.12f, occurrence.BitErrorRate);
        Assert.Null(occurrence.KindOverride);
        Assert.Equal(Evening.AddDays(1), occurrence.MatchedAt);
    }

    [Fact(DisplayName = "a copy kept before series names were made has its series name made from its programme name")]
    public void AnOldCopyHasItsSeriesNameMade()
    {
        CatalogOccurrence occurrence = CatalogOccurrence.Found(CatalogEntryId.New(), RecordingId.New(), Stretch, Copy("架空の物語 第3話「架空の副題」", null), 0, Evening);

        Assert.Equal("架空の物語", occurrence.SeriesName);
    }

    [Fact(DisplayName = "a stretch ends after it starts, at no time before the recording's own zero, and agrees to within three tenths")]
    public void AStretchIsBounded()
    {
        ProgrammeCopy programme = Copy("架空の物語", "架空の物語");

        Assert.Throws<ArgumentException>(() => Found(new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), programme, 0));
        Assert.Throws<ArgumentException>(() => Found(new SoundSpan(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10)), programme, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Found(Stretch, programme, 0.31));
        Assert.Throws<ArgumentOutOfRangeException>(() => Found(Stretch, programme, -0.01));
    }

    [Fact(DisplayName = "a kept stretch has an identity of its own")]
    public void AKeptStretchHasAnIdentity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(0));

        Assert.Equal(7, Rehydrated(7).Id);
    }

    [Fact(DisplayName = "the stretch tells series linking which recording, station, series and start it stands for")]
    public void TheStretchIsASeriesMember()
    {
        CatalogOccurrence occurrence = Found(Stretch, Copy("架空の物語 #3", "架空の物語"), 0);

        Assert.Equal(
            new SeriesMember(occurrence.RecordingId, new NetworkId(1), new ServiceId(2), "架空の物語", Evening.AddMinutes(-1)),
            occurrence.Member);
    }

    private static CatalogOccurrence Found(SoundSpan span, ProgrammeCopy programme, double bitErrorRate)
        => CatalogOccurrence.Found(CatalogEntryId.New(), RecordingId.New(), span, programme, bitErrorRate, Evening);

    private static CatalogOccurrence Rehydrated(long id)
        => CatalogOccurrence.Rehydrate(
            id,
            CatalogEntryId.New(),
            RecordingId.New(),
            Stretch.Starts,
            Stretch.Ends,
            new NetworkId(1),
            new ServiceId(2),
            "架空の物語",
            Evening,
            0.1f,
            SegmentKind.Break,
            Evening);

    private static ProgrammeCopy Copy(string name, string? seriesName)
        => new(
            new NetworkId(1),
            new ServiceId(2),
            Evening,
            Evening.AddMinutes(30),
            Evening.AddMinutes(-1),
            name,
            [],
            [],
            null,
            AudioMode.Stereo,
            seriesName);
}
