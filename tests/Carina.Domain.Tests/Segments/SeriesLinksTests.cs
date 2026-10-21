using Carina.Domain.Base;
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

    [Fact(DisplayName = "BR-CD-011: two recordings on one station of a programme the guide starts at the same time are one episode, and link whatever their names")]
    public void TwoRecordingsOfOneBroadcastAreOneEpisode()
    {
        SeriesMember first = Member(1, "架空の物語", Evening) with { ProgrammeStartsAt = Evening.AddSeconds(10) };
        SeriesMember again = Member(1, null, Evening.AddSeconds(4)) with { ProgrammeStartsAt = Evening.AddSeconds(10) };
        SeriesMember elsewhere = Member(2, "架空の物語", Evening) with { ProgrammeStartsAt = Evening.AddSeconds(10) };
        SeriesMember unplaced = Member(1, "架空の物語", Evening);

        Assert.Equal(first.Episode, again.Episode);
        Assert.True(SeriesLinks.None.Link(first, again));
        Assert.NotEqual(first.Episode, elsewhere.Episode);
        Assert.NotEqual(first.Episode, (first with { ProgrammeStartsAt = Evening.AddDays(7).AddSeconds(10) }).Episode);
        Assert.NotEqual(unplaced.Episode, Member(1, "架空の物語", Evening).Episode);
        Assert.False(SeriesLinks.None.Link(again, elsewhere));
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

    [Fact(DisplayName = "BR-CD-011: another episode in a weekly slot is on the station, a whole number of weeks away on the same day of the week, and no nearer than six days to an episode")]
    public void AnotherEpisodeInTheSlotIsAWeekAway()
    {
        SeriesMember[] series = [Member(1, "架空の物語", Evening), Member(1, "架空の物語", Evening.AddDays(7))];

        Assert.True(SeriesLinks.AnotherEpisodeInTheSlot(Member(1, "まるで違う架空の名前", Evening.AddDays(14).AddMinutes(80)), series));
        Assert.True(SeriesLinks.AnotherEpisodeInTheSlot(Member(1, null, Evening.AddDays(21).AddMinutes(-90)), series));
        Assert.False(SeriesLinks.AnotherEpisodeInTheSlot(Member(1, null, Evening.AddDays(15)), series));
        Assert.False(SeriesLinks.AnotherEpisodeInTheSlot(Member(1, null, Evening.AddDays(7).AddMinutes(60)), series));
        Assert.False(SeriesLinks.AnotherEpisodeInTheSlot(Member(1, null, Evening.AddDays(14).AddMinutes(100)), series));
        Assert.False(SeriesLinks.AnotherEpisodeInTheSlot(Member(2, null, Evening.AddDays(14)), series));
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

    [Fact(DisplayName = "the series of a recording is the others joined to it through one another, in the order handed over, never itself")]
    public void TheSeriesOfARecordingIsTheOthersJoinedToIt()
    {
        SeriesMember judged = Member(1, "架空の物語", Evening.AddDays(14).AddMinutes(160));
        SeriesMember week1 = Member(1, "架空の物語", Evening);
        SeriesMember week2 = Member(1, "架空の物語", Evening.AddDays(7).AddMinutes(80));
        SeriesMember elsewhere = Member(2, "架空の物語", Evening);
        SeriesMember another = Member(1, "架空のクイズ", Evening);

        Assert.Equal([week1, week2], SeriesLinks.None.SeriesOf(judged, [week1, elsewhere, judged, another, week2]));
        Assert.Empty(SeriesLinks.None.SeriesOf(judged, [elsewhere, another]));
    }

    [Fact(DisplayName = "BR-CD-011: two days of a daily programme kept under names that differ only in the subtitle are one series")]
    public void TwoDaysKeptUnderSubtitledNamesAreOneSeries()
    {
        SeriesMember monday = Daily(0, "架空の巻物の段").MemberAs(RecordingId.New());
        SeriesMember tuesday = Daily(1, "架空の手裏剣の段").MemberAs(RecordingId.New());

        Assert.Equal("アニメ 架空の忍者", monday.SeriesName);
        Assert.True(SeriesLinks.None.Link(monday, tuesday));
    }

    private static ProgrammeCopy Daily(int day, string subtitle)
    {
        DateTime starts = Evening.AddDays(day);
        string name = $"アニメ 架空の忍者「{subtitle}」";

        return new ProgrammeCopy(new NetworkId(1), new ServiceId(1), starts, starts.AddMinutes(10), starts.AddMinutes(-1), $"{name}[字]", [], [], null, AudioMode.Stereo, name);
    }

    private static SeriesMember Member(int station, string? series, DateTime startedAt)
        => new(RecordingId.New(), new NetworkId(station), new ServiceId(station), series, startedAt);
}
