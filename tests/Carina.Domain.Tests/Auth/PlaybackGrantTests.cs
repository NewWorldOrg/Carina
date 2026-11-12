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

    [Fact(DisplayName = "BR-PV-002: a subject holding fewer grants than the most forgets none to open another")]
    public void ASubjectHoldingFewerThanTheMostForgetsNone()
    {
        PlaybackGrant[] held = [.. Grants(Watcher, PlaybackGrantPolicy.MostHeldPerSubject - 1)];

        Assert.Null(PlaybackGrantPolicy.TheOneToForgetBeforeOpeningFor(Watcher, held));
    }

    [Fact(DisplayName = "BR-PV-002: a subject holding eight grants forgets the one opened first to open another")]
    public void ASubjectHoldingTheMostForgetsTheOneOpenedFirst()
    {
        Subject other = new("someone else");
        PlaybackGrant[] held =
        [
            PlaybackGrant.OpenedBy(Unguessable.Issue(), other, Seven, At.AddHours(-1)),
            .. Grants(Watcher, PlaybackGrantPolicy.MostHeldPerSubject).Reverse(),
        ];

        PlaybackGrant? forgotten = PlaybackGrantPolicy.TheOneToForgetBeforeOpeningFor(Watcher, held);

        Assert.NotNull(forgotten);
        Assert.Equal(Watcher, forgotten.Subject);
        Assert.Equal(At, forgotten.OpenedAt);
        Assert.Equal(8, PlaybackGrantPolicy.MostHeldPerSubject);
    }

    [Fact]
    public void GrantsOfOtherSubjectsDoNotCountTowardsTheMost()
    {
        PlaybackGrant[] held = [.. Grants(new Subject("someone else"), PlaybackGrantPolicy.MostHeldPerSubject)];

        Assert.Null(PlaybackGrantPolicy.TheOneToForgetBeforeOpeningFor(Watcher, held));
    }

    private static IEnumerable<PlaybackGrant> Grants(Subject subject, int count)
        => Enumerable.Range(0, count)
            .Select(minute => PlaybackGrant.OpenedBy(Unguessable.Issue(), subject, Seven, At.AddMinutes(minute)));
}
