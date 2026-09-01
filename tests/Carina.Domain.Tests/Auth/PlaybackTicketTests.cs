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
        IssuedPlaybackTicket issued = Issue();

        Assert.True(Unguessable.IsOne(issued.InTheClear));
        Assert.Equal(Unguessable.Length, issued.InTheClear.Length);
    }

    [Fact]
    public void NoTwoTicketsAreTheSame()
    {
        HashSet<string> seen = [];

        for (int drawn = 0; drawn < 1000; drawn++)
        {
            Assert.True(seen.Add(Issue().InTheClear));
        }
    }

    [Fact]
    public void WhatIsHeldIsADigestAndNotTheTicketItself()
    {
        IssuedPlaybackTicket issued = Issue();

        Assert.NotEqual(issued.InTheClear, issued.Held.Digest);
        Assert.Equal(PlaybackTicket.DigestOf(issued.InTheClear), issued.Held.Digest);
    }

    [Fact]
    public void NothingAHeldTicketCanBeAskedForGivesTheTicketBack()
    {
        IssuedPlaybackTicket issued = Issue();

        IEnumerable<string> answers = typeof(PlaybackTicket)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetValue(issued.Held)?.ToString() ?? string.Empty)
            .Append(issued.Held.ToString() ?? string.Empty);

        Assert.DoesNotContain(answers, answer => answer.Contains(issued.InTheClear, StringComparison.Ordinal));
    }

    [Fact]
    public void AnIssuedTicketSaysNothingWhenItIsPrinted()
    {
        IssuedPlaybackTicket issued = Issue();

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
        IssuedPlaybackTicket issued = PlaybackTicket.Issue(Watcher, target, Noon);

        Assert.Equal(Watcher, issued.Held.Subject);
        Assert.Equal(target, issued.Held.Target);
        Assert.Equal(Noon, issued.Held.IssuedAt);
    }

    [Fact]
    public void ATicketOpensTheOneThingItWasIssuedFor()
    {
        IssuedPlaybackTicket issued = PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon);

        Assert.True(issued.Held.Opens(PlaybackTarget.Recording("7")));
        Assert.False(issued.Held.Opens(PlaybackTarget.Recording("8")));
        Assert.False(issued.Held.Opens(PlaybackTarget.LiveChannel("7")));
    }

    [Fact]
    public void ATicketHasNotLapsedASecondBeforeItsLifetimeIsUp()
    {
        IssuedPlaybackTicket issued = Issue();

        Assert.False(issued.Held.HasLapsed(
            Noon + PlaybackTicketPolicy.Default.Lifetime - TimeSpan.FromSeconds(1),
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketHasLapsedTheMomentItsLifetimeIsUp()
    {
        IssuedPlaybackTicket issued = Issue();

        Assert.True(issued.Held.HasLapsed(
            Noon + PlaybackTicketPolicy.Default.Lifetime,
            PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketHasLapsedASecondAfterItsLifetimeIsUp()
    {
        IssuedPlaybackTicket issued = Issue();

        Assert.True(issued.Held.HasLapsed(
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
        Assert.Throws<ArgumentException>(
            () => PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), DateTime.SpecifyKind(Noon, DateTimeKind.Local)));

        IssuedPlaybackTicket issued = Issue();

        Assert.Throws<ArgumentException>(
            () => issued.Held.HasLapsed(DateTime.SpecifyKind(Noon, DateTimeKind.Unspecified), PlaybackTicketPolicy.Default));
    }

    [Fact]
    public void ATicketIsIssuedToSomeoneForSomething()
    {
        Assert.Throws<ArgumentNullException>(
            () => PlaybackTicket.Issue(null!, PlaybackTarget.Recording("7"), Noon));
        Assert.Throws<ArgumentNullException>(
            () => PlaybackTicket.Issue(Watcher, null!, Noon));
    }

    private static IssuedPlaybackTicket Issue()
        => PlaybackTicket.Issue(Watcher, PlaybackTarget.Recording("7"), Noon);
}
