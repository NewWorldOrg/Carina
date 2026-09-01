using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PlaybackGrantTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

    [Fact]
    public void AGrantLastsLongEnoughToWatchAFilmAndNoLonger()
    {
        Assert.Equal(TimeSpan.FromHours(2), PlaybackGrantPolicy.Default.Lifetime);
    }

    [Fact]
    public void ATicketStillLapsesInTheThirtySecondsItAlwaysDid()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), PlaybackTicketPolicy.Default.Lifetime);
    }

    [Fact]
    public void AGrantIsCarriedByTheTicketThatOpenedItAndNeverHoldsThatValue()
    {
        string carrier = Unguessable.Issue();
        PlaybackGrant opened = PlaybackGrant.OpenedBy(carrier, Watcher, Seven, At);

        Assert.NotEqual(carrier, opened.Digest);
        Assert.Equal(PlaybackTicket.DigestOf(carrier), opened.Digest);
    }

    [Fact]
    public void AGrantOpensTheOneRecordingItWasOpenedForAndNoOther()
    {
        PlaybackGrant opened = PlaybackGrant.OpenedBy(Unguessable.Issue(), Watcher, Seven, At);

        Assert.True(opened.Opens(Seven));
        Assert.False(opened.Opens(Eight));
    }

    [Fact]
    public void AGrantKnowsWhoseItIsSoItCanBeTakenBackWithTheirSession()
    {
        PlaybackGrant opened = PlaybackGrant.OpenedBy(Unguessable.Issue(), Watcher, Seven, At);

        Assert.True(opened.BelongsTo(Watcher));
        Assert.False(opened.BelongsTo(new Subject("somebody else")));
    }

    [Fact]
    public void AGrantLapsesAtTwoHoursAndNotAMomentBefore()
    {
        PlaybackGrant opened = PlaybackGrant.OpenedBy(Unguessable.Issue(), Watcher, Seven, At);

        Assert.Equal(At.AddHours(2), opened.LapsesAt(PlaybackGrantPolicy.Default));
        Assert.False(opened.HasLapsed(At.AddHours(2).AddSeconds(-1), PlaybackGrantPolicy.Default));
        Assert.True(opened.HasLapsed(At.AddHours(2), PlaybackGrantPolicy.Default));
    }

    [Fact]
    public void AGrantIsOpenedByAValueThatCouldHaveBeenIssuedAndByNothingElse()
    {
        Assert.Throws<ArgumentException>(
            () => PlaybackGrant.OpenedBy("not-a-ticket", Watcher, Seven, At));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALifetimeThatIsNoTimeAtAllIsNotALifetime(int hours)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackGrantPolicy(TimeSpan.FromHours(hours)));
    }

    [Fact]
    public void AGrantCannotBeMadeToOutliveTheDayItWasOpenedIn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackGrantPolicy(PlaybackGrantPolicy.LongestLifetime + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void AGrantIsOpenedAtAnInstantInUtcAndNotAtALocalOne()
    {
        Assert.Throws<ArgumentException>(() => PlaybackGrant.OpenedBy(
            Unguessable.Issue(),
            Watcher,
            Seven,
            new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local)));
    }
}
