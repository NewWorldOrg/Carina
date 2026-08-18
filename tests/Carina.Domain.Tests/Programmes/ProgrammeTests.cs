using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeTests
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeId Id = new(new NetworkId(32739), new ServiceId(1049), new EventId(47289));

    private static readonly TransportStreamId Stream = new(32739);

    [Fact]
    public void AProgrammeTakesTheNameAndTimesItWasBroadcastWith()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.Equal(Id, programme.Id);
        Assert.Equal(Stream, programme.TransportStreamId);
        Assert.Equal(At.AddHours(22), programme.StartsAt);
        Assert.Equal(At.AddHours(23), programme.EndsAt);
        Assert.Equal("Now", programme.Name);
        Assert.Equal("What it is about", programme.Summary);
        Assert.False(programme.IsShadow);
        Assert.Equal(At, programme.UpdatedAt);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stream")]
    [InlineData("name")]
    [InlineData("summary")]
    [InlineData("shadow")]
    [InlineData("end")]
    [InlineData("genres")]
    [InlineData("items")]
    [InlineData("related")]
    [InlineData("subtitles")]
    [InlineData("source")]
    public void AnyOneFieldMovingOnItsOwnCountsAsAChange(string moved)
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.True(programme.Absorb(Moved(moved), At.AddHours(1)));
        Assert.Equal(At.AddHours(1), programme.UpdatedAt);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("stream")]
    [InlineData("name")]
    [InlineData("summary")]
    [InlineData("shadow")]
    [InlineData("end")]
    public void TheOneFieldThatMovedIsTheOneThatChanged(string moved)
    {
        var programme = Programme.Discover(Broadcast(), At);

        programme.Absorb(Moved(moved), At.AddHours(1));

        Assert.Equal(moved == "start" ? At.AddHours(20) : At.AddHours(22), programme.StartsAt);
        Assert.Equal(moved == "stream" ? new TransportStreamId(32740) : Stream, programme.TransportStreamId);
        Assert.Equal(moved == "name" ? "Renamed" : "Now", programme.Name);
        Assert.Equal(moved == "summary" ? "Another summary" : "What it is about", programme.Summary);
        Assert.Equal(moved == "shadow", programme.IsShadow);
    }

    [Fact]
    public void ATimeThatMovedIsTakenAsBroadcastRatherThanTreatedAsSuspect()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.True(programme.Absorb(
            Broadcast(startsAt: At.AddHours(23), endsAt: At.AddHours(24)),
            At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.StartsAt);
        Assert.Equal(At.AddHours(24), programme.EndsAt);
    }

    [Fact]
    public void AnEmptyNameDoesNotEraseTheOneAlreadyKnown()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.False(programme.Absorb(Broadcast(name: string.Empty), At.AddHours(1)));

        Assert.Equal("Now", programme.Name);
        Assert.Equal(At, programme.UpdatedAt);
    }

    [Fact]
    public void AnEmptySummaryDoesNotEraseTheOneAlreadyKnown()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.False(programme.Absorb(Broadcast(summary: string.Empty), At.AddHours(1)));

        Assert.Equal("What it is about", programme.Summary);
    }

    [Fact]
    public void AnEndThatIsNotKnownYetDoesNotEraseTheOneAlreadySettled()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.False(programme.Absorb(Broadcast(openEnded: true), At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.EndsAt);
    }

    [Fact]
    public void AProgrammeWhoseEndIsStillOpenSaysSoRatherThanGuessing()
    {
        Assert.Null(Programme.Discover(Broadcast(openEnded: true), At).EndsAt);
    }

    [Fact]
    public void AnEndArrivingLaterSettlesTheOneThatWasOpen()
    {
        var programme = Programme.Discover(Broadcast(openEnded: true), At);

        Assert.True(programme.Absorb(Broadcast(), At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.EndsAt);
    }

    [Fact]
    public void AStartMovingPastTheEndLeavesNoEndRatherThanOneBeforeTheStart()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.True(programme.Absorb(Broadcast(startsAt: At.AddHours(23), openEnded: true), At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.StartsAt);
        Assert.Null(programme.EndsAt);
    }

    [Fact]
    public void AnEndThatIsNotAfterTheStartIsNotAnEnd()
    {
        Assert.Null(Programme.Discover(Broadcast(endsAt: At.AddHours(22)), At).EndsAt);
    }

    [Fact]
    public void TheSameBroadcastArrivingAgainChangesNothing()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.False(programme.Absorb(Broadcast(), At.AddHours(1)));

        Assert.Equal(At, programme.UpdatedAt);
    }

    [Fact]
    public void ABroadcastOfAnotherProgrammeIsRefused()
    {
        var programme = Programme.Discover(Broadcast(), At);

        var another = Broadcast() with
        {
            Id = new ProgrammeId(new NetworkId(32739), new ServiceId(1049), new EventId(47290)),
        };

        Assert.Throws<ArgumentException>(() => programme.Absorb(another, At));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("summary")]
    public void TextLongerThanThisSystemKeepsIsCutOnTheWayIn(string field)
    {
        var most = field == "name" ? Programme.NameMaxLength : Programme.SummaryMaxLength;
        var overlong = new string('あ', most + 10);

        var programme = Programme.Discover(
            field == "name" ? Broadcast(name: overlong) : Broadcast(summary: overlong),
            At);

        Assert.Equal(most, field == "name" ? programme.Name.Length : programme.Summary.Length);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("summary")]
    public void TextAlreadyCutIsNotSeenAsChangingWhenItArrivesAgain(string field)
    {
        var most = field == "name" ? Programme.NameMaxLength : Programme.SummaryMaxLength;
        var overlong = new string('あ', most + 10);
        var broadcast = field == "name" ? Broadcast(name: overlong) : Broadcast(summary: overlong);

        var programme = Programme.Discover(broadcast, At);

        Assert.False(programme.Absorb(broadcast, At.AddHours(1)));
    }

    [Fact]
    public void ATimeThatIsNotInUniversalTimeIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => Programme.Discover(Broadcast(), new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Local)));

        Assert.Throws<ArgumentException>(
            () => Programme.Discover(
                Broadcast(startsAt: new DateTime(2026, 8, 18, 22, 0, 0, DateTimeKind.Unspecified)),
                At));
    }

    [Theory]
    [InlineData("genres")]
    [InlineData("items")]
    [InlineData("related")]
    public void AnEmptyListDoesNotEraseTheOneAlreadyKnown(string field)
    {
        var programme = Programme.Discover(Moved(field), At);

        Assert.False(programme.Absorb(Broadcast(), At.AddHours(1)));

        Assert.NotEmpty(field switch
        {
            "genres" => programme.Genres.Cast<object>(),
            "items" => programme.Items,
            _ => programme.Related,
        });
        Assert.Equal(At, programme.UpdatedAt);
    }

    [Fact]
    public void AProgrammeCarriesWhatItWasBroadcastWithBesidesItsName()
    {
        var programme = Programme.Discover(
            Broadcast() with
            {
                Genres = [new ProgrammeGenre(0, 1)],
                Items = [new ProgrammeItem("Heading", "Body")],
                Related = [new RelatedProgramme(32739, 1048, 47289, RelationKind.Shared)],
                HasSubtitles = true,
                Source = ProgrammeSource.PresentFollowing,
            },
            At);

        Assert.Equal(new ProgrammeGenre(0, 1), Assert.Single(programme.Genres));
        Assert.Equal(new ProgrammeItem("Heading", "Body"), Assert.Single(programme.Items));
        Assert.Equal(RelationKind.Shared, Assert.Single(programme.Related).Kind);
        Assert.True(programme.HasSubtitles);
        Assert.Equal(ProgrammeSource.PresentFollowing, programme.Source);
    }

    [Fact]
    public void AProgrammeThatIsOnlyAPlaceholderIsKeptAndMarkedAsOne()
    {
        var programme = Programme.Discover(Broadcast(name: string.Empty, isShadow: true), At);

        Assert.True(programme.IsShadow);
        Assert.Equal(string.Empty, programme.Name);
    }

    private static ProgrammeBroadcast Moved(string field)
        => field switch
        {
            "start" => Broadcast(startsAt: At.AddHours(20), endsAt: At.AddHours(23)),
            "stream" => Broadcast() with { TransportStreamId = new TransportStreamId(32740) },
            "name" => Broadcast(name: "Renamed"),
            "summary" => Broadcast(summary: "Another summary"),
            "shadow" => Broadcast(isShadow: true),
            "genres" => Broadcast() with { Genres = [new ProgrammeGenre(0, 1)] },
            "items" => Broadcast() with { Items = [new ProgrammeItem("Heading", "Body")] },
            "related" => Broadcast() with
            {
                Related = [new RelatedProgramme(32739, 1048, 47289, RelationKind.Shared)],
            },
            "subtitles" => Broadcast() with { HasSubtitles = true },
            "source" => Broadcast() with { Source = ProgrammeSource.ScheduleExtended },
            _ => Broadcast(endsAt: At.AddHours(24)),
        };

    private static ProgrammeBroadcast Broadcast(
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        bool openEnded = false,
        string name = "Now",
        string summary = "What it is about",
        bool isShadow = false)
        => new(
            Id,
            Stream,
            startsAt ?? At.AddHours(22),
            openEnded ? null : endsAt ?? At.AddHours(23),
            name,
            summary,
            isShadow);
}
