using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SeriesLinksTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "two recordings on the same station under the same series name and about the same time are one series")]
    public void TheSameNameStationAndTimeAreOneSeries()
    {
        SeriesMember first = Member(1, "架空の物語", Evening);
        SeriesMember second = Member(1, "架空の物語", Evening.AddDays(7).AddMinutes(30));

        Assert.True(SeriesLinks.None.Link(first, second));
    }

    [Fact(DisplayName = "a series name that begins another by at least four characters links them")]
    public void ASharedHeadOfFourCharactersLinks()
    {
        SeriesMember shorter = Member(1, "架空の物語", Evening);
        SeriesMember longer = Member(1, "架空の物語スペシャル", Evening.AddDays(7));

        Assert.True(SeriesLinks.None.Link(shorter, longer));
        Assert.True(SeriesNames.Link("架空の物", "架空の物語"));
        Assert.False(SeriesNames.Link("架空の", "架空の物語"));
        Assert.False(SeriesNames.Link("架空の物語A", "架空の物語B"));
        Assert.False(SeriesNames.Link(null, "架空の物語"));
    }

    [Fact(DisplayName = "another station, another time of day or another name is another series")]
    public void AnotherStationTimeOrNameIsAnotherSeries()
    {
        SeriesMember first = Member(1, "架空の物語", Evening);

        Assert.False(SeriesLinks.None.Link(first, Member(2, "架空の物語", Evening.AddDays(7))));
        Assert.False(SeriesLinks.None.Link(first, Member(1, "架空の物語", Evening.AddDays(7).AddMinutes(91))));
        Assert.False(SeriesLinks.None.Link(first, Member(1, "別の架空の話", Evening.AddDays(7))));
    }

    [Fact(DisplayName = "the time of day is compared around midnight")]
    public void TheTimeOfDayIsComparedAroundMidnight()
    {
        DateTime lateNight = new(2026, 10, 5, 23, 30, 0, DateTimeKind.Utc);

        Assert.True(SeriesLinks.None.Link(Member(1, "架空の物語", lateNight), Member(1, "架空の物語", lateNight.AddDays(7).AddMinutes(60))));
    }

    [Fact(DisplayName = "two stretches of one recording are always one series, even with no series name")]
    public void OneRecordingIsOneSeries()
    {
        RecordingId recording = RecordingId.New();
        SeriesMember first = new(recording, new NetworkId(1), new ServiceId(1), null, Evening);
        SeriesMember second = new(recording, new NetworkId(1), new ServiceId(1), null, Evening);

        Assert.True(SeriesLinks.None.Link(first, second));
        Assert.False(SeriesLinks.None.Link(first, Member(1, null, Evening)));
    }

    [Fact(DisplayName = "recordings that share the sound of a programme fixture are one series whatever their names")]
    public void SharedFixtureSoundLinksRecordings()
    {
        SeriesMember first = Member(1, "架空の物語", Evening);
        SeriesMember renamed = Member(1, "まるで違う架空の名前", Evening.AddDays(7));
        SeriesLinks links = SeriesLinks.SharingSound([[first.RecordingId, renamed.RecordingId]]);

        Assert.False(SeriesLinks.None.Link(first, renamed));
        Assert.True(links.Link(first, renamed));
    }

    [Fact(DisplayName = "linked recordings are grouped through one another")]
    public void LinkedRecordingsAreGroupedThroughOneAnother()
    {
        SeriesMember first = Member(1, "架空の物語", Evening);
        SeriesMember second = Member(1, "架空の物語", Evening.AddDays(7).AddMinutes(80));
        SeriesMember third = Member(1, "架空の物語", Evening.AddDays(14).AddMinutes(160));
        SeriesMember elsewhere = Member(2, "架空の物語", Evening);

        IReadOnlyList<IReadOnlyList<SeriesMember>> groups = SeriesLinks.None.Group([first, second, third, elsewhere]);

        Assert.Equal(2, groups.Count);
        Assert.Equal([first, second, third], groups[0]);
        Assert.Equal([elsewhere], groups[1]);
    }

    private static SeriesMember Member(int station, string? series, DateTime startedAt)
        => new(RecordingId.New(), new NetworkId(station), new ServiceId(station), series, startedAt);
}
