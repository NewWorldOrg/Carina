using System.Reflection;

using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class PlaybackTicketTests
{
    private static readonly DateTime Noon = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    [Fact]
    public void AnIssuedTicketIsAsUnguessableAsASessionId()
    {
        Issue(out string inTheClear);

        Assert.True(Unguessable.IsOne(inTheClear));
        Assert.Equal(Unguessable.Length, inTheClear.Length);
    }

    [Fact]
    public void NoTwoTicketsAreTheSame()
    {
        HashSet<string> seen = [];

        for (int drawn = 0; drawn < 1000; drawn++)
        {
            Issue(out string inTheClear);

            Assert.True(seen.Add(inTheClear));
        }
    }

    [Fact]
    public void WhatIsHeldIsADigestAndNotTheTicketItself()
    {
        PlaybackTicket held = Issue(out string inTheClear);

        Assert.NotEqual(inTheClear, held.Digest);
        Assert.Equal(PlaybackTicket.DigestOf(inTheClear), held.Digest);
    }

    [Fact]
    public void NothingAHeldTicketCanBeAskedForGivesTheTicketBack()
    {
        PlaybackTicket held = Issue(out string inTheClear);

        IEnumerable<string> answers = typeof(PlaybackTicket)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetValue(held)?.ToString() ?? string.Empty)
            .Append(held.ToString() ?? string.Empty);

        Assert.DoesNotContain(answers, answer => answer.Contains(inTheClear, StringComparison.Ordinal));
    }

    [Fact]
    public void AnIssuedTicketSaysNothingWhenItIsPrinted()
    {
        IssuedPlaybackTicket issued = new(Unguessable.Issue(), Noon);

        Assert.DoesNotContain(issued.InTheClear, issued.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ADigestIsTheSameAnswerEveryTimeAndADifferentOneForADifferentTicket()
    {
        Assert.Equal(PlaybackTicket.DigestOf("one"), PlaybackTicket.DigestOf("one"));
        Assert.NotEqual(PlaybackTicket.DigestOf("one"), PlaybackTicket.DigestOf("two"));
    }

    [Fact]
    public void ATicketCarriesWhoAskedForItAndWhatItOpens()
    {
        PlaybackTarget target = PlaybackTarget.LiveChannel("31");
        PlaybackTicket held = PlaybackTicket.Issue(Watcher, target, Noon, out _);

        Assert.Equal(Watcher, held.Subject);
        Assert.Equal(target, held.Target);
        Assert.Equal(Noon, held.IssuedAt);
    }

    [Fact]
    public void ATicketOpensTheOneThingItWasIssuedFor()
    {
        PlaybackTicket held = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);

        Assert.True(held.Opens(PlaybackTarget.Recording("7")));
        Assert.False(held.Opens(PlaybackTarget.Recording("8")));
        Assert.False(held.Opens(PlaybackTarget.LiveChannel("7")));
    }

    [Fact]
    public void ATicketDiesALifetimeAfterItWasIssued()
    {
        PlaybackTicket held = Issue(out _);

        Assert.Equal(Noon + PlaybackTicketPolicy.Default.Lifetime, held.LapsesAt(PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketHasNotLapsedASecondBeforeItsLifetimeIsUp()
    {
        PlaybackTicket held = Issue(out _);

        Assert.False(held.HasLapsed(
            Noon + PlaybackTicketPolicy.Default.Lifetime - TimeSpan.FromSeconds(1),
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketHasLapsedTheMomentItsLifetimeIsUp()
    {
        PlaybackTicket held = Issue(out _);

        Assert.True(held.HasLapsed(
            Noon + PlaybackTicketPolicy.Default.Lifetime,
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketHasLapsedASecondAfterItsLifetimeIsUp()
    {
        PlaybackTicket held = Issue(out _);

        Assert.True(held.HasLapsed(
            Noon + PlaybackTicketPolicy.Default.Lifetime + TimeSpan.FromSeconds(1),
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketLivesForThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), PlaybackTicketPolicy.Default.Lifetime);
    }

    [Fact]
    public void ALifetimeThatIsNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackTicketPolicy(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackTicketPolicy(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ALifetimeLongEnoughToOutliveAStolenUrlIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PlaybackTicketPolicy(PlaybackTicketPolicy.LongestLifetime + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ATicketIsIssuedAgainstAClockKeptInUtc()
    {
        Assert.Throws<ArgumentException>(() => PlaybackTicket.Issue(
            Watcher,
            PlaybackTarget.Recording("7"),
            DateTime.SpecifyKind(Noon, DateTimeKind.Local),
            out _));

        PlaybackTicket held = Issue(out _);

        Assert.Throws<ArgumentException>(() => held.HasLapsed(
            DateTime.SpecifyKind(Noon, DateTimeKind.Unspecified),
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketIsIssuedToSomeoneForSomething()
    {
        Assert.Throws<ArgumentNullException>(
            () => PlaybackTicket.Issue(null!, PlaybackTarget.Recording("7"), Noon, out _));
        Assert.Throws<ArgumentNullException>(
            () => PlaybackTicket.Issue(Watcher, null!, Noon, out _));
    }

    [Fact(DisplayName = "BR-PV-002: a ticket lets the caller in once, on what it was issued for, within its lifetime")]
    public void ATicketLetsTheCallerInOnce()
    {
        PlaybackTicket held = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);

        Assert.True(held.Spend(PlaybackTarget.Recording("7"), Noon, PlaybackTicketPolicy.Default));
        Assert.False(held.Spend(PlaybackTarget.Recording("7"), Noon, PlaybackTicketPolicy.Default));
    }

    [Fact(DisplayName = "BR-PV-002: a ticket spent on the wrong target is spent all the same")]
    public void ATicketSpentOnTheWrongTargetIsSpentAllTheSame()
    {
        PlaybackTicket held = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);

        Assert.False(held.Spend(PlaybackTarget.Recording("8"), Noon, PlaybackTicketPolicy.Default));
        Assert.False(held.Spend(PlaybackTarget.Recording("7"), Noon, PlaybackTicketPolicy.Default));
    }

    [Fact(DisplayName = "BR-PV-002: a ticket lets nobody in the moment its lifetime is up")]
    public void ATicketLetsNobodyInTheMomentItsLifetimeIsUp()
    {
        PlaybackTicket before = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);
        PlaybackTicket at = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);
        TimeSpan lifetime = PlaybackTicketPolicy.Default.Lifetime;

        Assert.True(before.Spend(
            PlaybackTarget.Recording("7"),
            Noon + lifetime - TimeSpan.FromTicks(1),
            PlaybackTicketPolicy.Default));
        Assert.False(at.Spend(PlaybackTarget.Recording("7"), Noon + lifetime, PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void TicketsSpentFromManyThreadsAtOnceLetOneCallerIn()
    {
        PlaybackTicket held = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out _);

        bool[] answered = [.. Enumerable.Range(0, 32)
            .AsParallel()
            .Select(_ => held.Spend(PlaybackTarget.Recording("7"), Noon, PlaybackTicketPolicy.Default))];

        Assert.Single(answered, admitted => admitted);
    }

    [Theory(DisplayName = "BR-PV-002: a subject is issued another ticket while it holds fewer than eight")]
    [InlineData(PlaybackTicketPolicy.MostHeldPerSubject - 1, true)]
    [InlineData(PlaybackTicketPolicy.MostHeldPerSubject, false)]
    [InlineData(PlaybackTicketPolicy.MostHeldPerSubject + 1, false)]
    public void ASubjectIsIssuedAnotherTicketWhileItHoldsFewerThanTheMost(int holding, bool room)
    {
        Subject other = new("someone else");
        PlaybackTicket[] held =
        [
            .. Enumerable.Range(0, holding).Select(_ => IssuedTo(Watcher)),
            .. Enumerable.Range(0, PlaybackTicketPolicy.MostHeldPerSubject).Select(_ => IssuedTo(other)),
        ];

        Assert.Equal(room, PlaybackTicketPolicy.HasRoomFor(Watcher, held));
        Assert.Equal(8, PlaybackTicketPolicy.MostHeldPerSubject);
    }

    private static PlaybackTicket IssuedTo(Subject subject)
        => PlaybackTicket.Issue(subject, PlaybackTarget.Recording("7"), Noon, out string _);

    private static PlaybackTicket Issue(out string inTheClear)
        => PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon, out inTheClear);
}
