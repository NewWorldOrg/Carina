using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Unit.Programmes;

[Trait("Category", "Unit")]
public sealed class ProgrammeBroadcastTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeId Self = new(new NetworkId(32739), new ServiceId(1049), new EventId(47289));

    [Theory]
    [InlineData(-24)]
    [InlineData(-23)]
    [InlineData(0)]
    [InlineData(9 * 24)]
    [InlineData(10 * 24)]
    public void BrEv001AStartFromADayBehindToTenDaysAheadIsWithinTheHorizon(int hours)
        => Assert.True(Broadcast(Now.AddHours(hours)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartFurtherBackThanADayIsOutsideTheHorizon()
        => Assert.False(Broadcast(Now.AddDays(-1).AddSeconds(-1)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartFurtherAheadThanTenDaysIsOutsideTheHorizon()
        => Assert.False(Broadcast(Now.AddDays(10).AddSeconds(1)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001AStartOnTheDayTheBroadcastCalendarBeganIsOutsideTheHorizon()
        => Assert.False(
            Broadcast(new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc)).StartsWithinTheHorizonAt(Now));

    [Fact]
    public void BrEv001TheHorizonReachesPastTheCoverageAimedFor()
        => Assert.True(ProgrammeBroadcast.FurthestAhead > new CollectionSettings().WantedCoverage);

    [Fact]
    public void BrEv001TheHorizonIsJudgedAgainstAUtcClock()
        => Assert.Throws<ArgumentException>(() =>
            Broadcast(Now).StartsWithinTheHorizonAt(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local)));

    [Fact(DisplayName = "BR-ED-003: a programme with no name that shares an event is a shadow")]
    public void AProgrammeWithNoNameThatSharesAnEventIsAShadow()
        => Assert.True(Announced(string.Empty, Group(RelationKind.Shared, Here(1048, 100))).IsShadow);

    [Fact(DisplayName = "BR-ED-003: a programme that shares an event and has a name is not a shadow")]
    public void AProgrammeWithANameIsNotAShadow()
        => Assert.False(Announced("ニュース", Group(RelationKind.Shared, Here(1048, 100))).IsShadow);

    [Fact(DisplayName = "BR-ED-003: a programme with no name is a shadow even when the shared group names only itself")]
    public void AProgrammeWithNoNameSharingOnlyWithItselfIsAShadow()
    {
        ProgrammeBroadcast broadcast = Announced(string.Empty, Group(RelationKind.Shared, Here(1049, 47289)));

        Assert.True(broadcast.IsShadow);
        Assert.Empty(broadcast.Related);
    }

    [Theory(DisplayName = "BR-ED-003: a programme with no name that is only relayed or moved is not a shadow")]
    [InlineData(RelationKind.Relayed)]
    [InlineData(RelationKind.Moved)]
    public void AProgrammeWithNoNameThatIsOnlyRelayedOrMovedIsNotAShadow(RelationKind kind)
        => Assert.False(Announced(string.Empty, Group(kind, Here(1048, 100))).IsShadow);

    [Fact(DisplayName = "BR-ED-004: the related programmes are every event of its groups but itself, on this network or another")]
    public void TheRelatedProgrammesAreEveryEventOfItsGroupsButItself()
    {
        ProgrammeBroadcast broadcast = Announced(
            "ニュース",
            Group(RelationKind.Shared, Here(1049, 47289), Here(1048, 100)),
            Group(RelationKind.Relayed, Elsewhere(4, 101, 200)),
            Group(RelationKind.Moved, Here(1050, 300), Elsewhere(32739, 1049, 47289)));

        Assert.Equal(
            [
                new RelatedProgramme(32739, 1048, 100, RelationKind.Shared),
                new RelatedProgramme(4, 101, 200, RelationKind.Relayed),
                new RelatedProgramme(32739, 1050, 300, RelationKind.Moved),
                new RelatedProgramme(32739, 1049, 47289, RelationKind.Moved),
            ],
            broadcast.Related);
    }

    [Fact(DisplayName = "BR-ED-004: how a programme is running is believed from the present and following table only")]
    public void HowAProgrammeIsRunningIsBelievedFromThePresentAndFollowingTableOnly()
    {
        Assert.Equal(
            ProgrammeRunning.Running,
            Announced(ProgrammeSource.PresentFollowing, ProgrammeRunning.Running).Running);
        Assert.Equal(
            ProgrammeRunning.Undetermined,
            Announced(ProgrammeSource.ScheduleBasic, ProgrammeRunning.Running).Running);
        Assert.Equal(
            ProgrammeRunning.Undetermined,
            Announced(ProgrammeSource.ScheduleExtended, ProgrammeRunning.Running).Running);
    }

    [Fact(DisplayName = "BR-ED-002: two tables of one visit are joined on the one that names the programme")]
    public void TwoTablesAreJoinedOnTheOneThatNamesTheProgramme()
    {
        ProgrammeBroadcast named = Broadcast(Now) with
        {
            Name = "ニュース",
            EndsAt = null,
            Summary = string.Empty,
            Source = ProgrammeSource.ScheduleBasic,
        };
        ProgrammeBroadcast unnamed = Broadcast(Now.AddMinutes(1)) with
        {
            Name = string.Empty,
            EndsAt = Now.AddMinutes(31),
            Summary = "概要",
            Source = ProgrammeSource.PresentFollowing,
        };

        ProgrammeBroadcast joined = unnamed.MergedWith(named);

        Assert.Equal("ニュース", joined.Name);
        Assert.Equal(Now, joined.StartsAt);
        Assert.Equal(ProgrammeSource.ScheduleBasic, joined.Source);
        Assert.Equal(Now.AddMinutes(31), joined.EndsAt);
        Assert.Equal("概要", joined.Summary);

        ProgrammeBroadcast other = named.MergedWith(unnamed);

        Assert.Equal("ニュース", other.Name);
        Assert.Equal(Now, other.StartsAt);
        Assert.Equal(ProgrammeSource.ScheduleBasic, other.Source);
        Assert.Equal(Now.AddMinutes(31), other.EndsAt);
        Assert.Equal("概要", other.Summary);
    }

    [Fact(DisplayName = "BR-ED-002: what the earlier table held is kept, and what it lacked is taken from the later one")]
    public void WhatTheEarlierTableHeldIsKept()
    {
        ProgrammeBroadcast seen = Broadcast(Now) with
        {
            Genres = [new ProgrammeGenre(7, 0)],
            Audio = AudioMode.Stereo,
            Sounds = 0,
            Video = VideoMode.Undetermined,
            Running = ProgrammeRunning.Undetermined,
        };
        ProgrammeBroadcast arriving = Broadcast(Now) with
        {
            Genres = [new ProgrammeGenre(1, 0)],
            Items = [new ProgrammeItem("出演", "名前")],
            Related = [new RelatedProgramme(1, 2, 3, RelationKind.Shared)],
            HasSubtitles = true,
            Audio = AudioMode.Mono,
            Sounds = 2,
            Video = VideoMode.Interlaced480,
            Aspect = AspectRatio.SixteenByNine,
            Running = ProgrammeRunning.Running,
        };

        ProgrammeBroadcast merged = seen.MergedWith(arriving);

        Assert.Equal([new ProgrammeGenre(7, 0)], merged.Genres);
        Assert.Equal([new ProgrammeItem("出演", "名前")], merged.Items);
        Assert.Equal([new RelatedProgramme(1, 2, 3, RelationKind.Shared)], merged.Related);
        Assert.True(merged.HasSubtitles);
        Assert.Equal(AudioMode.Stereo, merged.Audio);
        Assert.Equal(2, merged.Sounds);
        Assert.Equal(VideoMode.Interlaced480, merged.Video);
        Assert.Equal(AspectRatio.SixteenByNine, merged.Aspect);
        Assert.Equal(ProgrammeRunning.Running, merged.Running);
    }

    [Theory(DisplayName = "BR-ED-002: a programme is a shadow after joining only when both tables said so")]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void AProgrammeIsAShadowAfterJoiningOnlyWhenBothSaidSo(bool seen, bool arriving, bool shadow)
        => Assert.Equal(
            shadow,
            (Broadcast(Now) with { IsShadow = seen }).MergedWith(Broadcast(Now) with { IsShadow = arriving }).IsShadow);

    private static ProgrammeBroadcast Announced(string name, params AnnouncedEventGroup[] groups)
        => ProgrammeBroadcast.Announced(
            Self,
            new TransportStreamId(32739),
            Now,
            Now.AddMinutes(30),
            name,
            string.Empty,
            ProgrammeSource.ScheduleBasic,
            ProgrammeRunning.Undetermined,
            groups);

    private static ProgrammeBroadcast Announced(ProgrammeSource source, ProgrammeRunning running)
        => ProgrammeBroadcast.Announced(
            Self,
            new TransportStreamId(32739),
            Now,
            Now.AddMinutes(30),
            "ニュース",
            string.Empty,
            source,
            running,
            []);

    private static AnnouncedEventGroup Group(RelationKind kind, params AnnouncedEvent[] events)
        => new(kind, events);

    private static AnnouncedEvent Here(int serviceId, int eventId)
        => new(null, serviceId, eventId);

    private static AnnouncedEvent Elsewhere(int networkId, int serviceId, int eventId)
        => new(networkId, serviceId, eventId);

    private static ProgrammeBroadcast Broadcast(DateTime startsAt)
        => new(
            Self,
            new TransportStreamId(32739),
            startsAt,
            startsAt.AddMinutes(30),
            "Now",
            string.Empty,
            IsShadow: false);
}
