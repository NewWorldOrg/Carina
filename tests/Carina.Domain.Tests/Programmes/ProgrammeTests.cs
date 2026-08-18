using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeTests
{
    private static readonly DateTime At = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeId Id = new(new NetworkId(32739), new ServiceId(1049), new EventId(47289));

    [Fact]
    public void AProgrammeTakesTheNameAndTimesItWasBroadcastWith()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.Equal(Id, programme.Id);
        Assert.Equal(At.AddHours(22), programme.StartsAt);
        Assert.Equal(At.AddHours(23), programme.EndsAt);
        Assert.Equal("Now", programme.Name);
        Assert.Equal("What it is about", programme.Summary);
        Assert.False(programme.IsShadow);
    }

    [Fact]
    public void ATimeThatMovedIsTakenAsBroadcastRatherThanTreatedAsSuspect()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.True(programme.Absorb(Broadcast(startsAt: At.AddHours(23)), At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.StartsAt);
        Assert.Equal(At.AddHours(1), programme.UpdatedAt);
    }

    [Fact]
    public void AnEmptyNameDoesNotEraseTheOneAlreadyKnown()
    {
        var programme = Programme.Discover(Broadcast(), At);

        Assert.False(programme.Absorb(Broadcast(name: string.Empty, summary: string.Empty), At.AddHours(1)));

        Assert.Equal("Now", programme.Name);
        Assert.Equal("What it is about", programme.Summary);
        Assert.Equal(At, programme.UpdatedAt);
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
        var programme = Programme.Discover(Broadcast(openEnded: true), At);

        Assert.Null(programme.EndsAt);
    }

    [Fact]
    public void AnEndArrivingLaterSettlesTheOneThatWasOpen()
    {
        var programme = Programme.Discover(Broadcast(openEnded: true), At);

        Assert.True(programme.Absorb(Broadcast(), At.AddHours(1)));

        Assert.Equal(At.AddHours(23), programme.EndsAt);
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

    [Fact]
    public void ANameLongerThanThisSystemKeepsIsCutRatherThanRefused()
    {
        var programme = Programme.Discover(Broadcast(name: new string('あ', Programme.NameMaxLength + 10)), At);

        Assert.Equal(Programme.NameMaxLength, programme.Name.Length);
    }

    [Fact]
    public void AProgrammeThatIsOnlyAPlaceholderIsKeptAndMarkedAsOne()
    {
        var programme = Programme.Discover(Broadcast(name: string.Empty, isShadow: true), At);

        Assert.True(programme.IsShadow);
        Assert.Equal(string.Empty, programme.Name);
    }

    [Fact]
    public void APlaceholderThatBecomesARealProgrammeStopsBeingOne()
    {
        var programme = Programme.Discover(Broadcast(name: string.Empty, isShadow: true), At);

        Assert.True(programme.Absorb(Broadcast(), At.AddHours(1)));

        Assert.False(programme.IsShadow);
        Assert.Equal("Now", programme.Name);
    }

    private static ProgrammeBroadcast Broadcast(
        DateTime? startsAt = null,
        bool openEnded = false,
        string name = "Now",
        string summary = "What it is about",
        bool isShadow = false)
        => new(
            Id,
            32739,
            startsAt ?? At.AddHours(22),
            openEnded ? null : (startsAt ?? At.AddHours(22)).AddHours(1),
            name,
            summary,
            isShadow);
}
