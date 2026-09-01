using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class PlaybackGrantStoreTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly Subject Somebody = new("somebody else");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

    [Fact]
    public void NothingIsAdmittedBeforeAnybodyHasEntered()
    {
        Assert.Null(Store(out _).Admit(Unguessable.Issue(), Seven));
    }

    [Fact]
    public void WhatEnteringOpensAdmitsTheSameCarrierAgainAndAgain()
    {
        PlaybackGrantStore store = Store(out _);
        string carrier = Unguessable.Issue();

        store.Open(carrier, Watcher, Seven);

        Assert.All(
            Enumerable.Range(0, 50),
            _ => Assert.Equal(Watcher, store.Admit(carrier, Seven)));
    }

    [Fact]
    public void AGrantForOneRecordingDoesNotOpenAnother()
    {
        PlaybackGrantStore store = Store(out _);
        string carrier = Unguessable.Issue();

        store.Open(carrier, Watcher, Seven);

        Assert.Null(store.Admit(carrier, Eight));
        Assert.Equal(Watcher, store.Admit(carrier, Seven));
    }

    [Fact]
    public void SomethingThatWasNeverIssuedIsNotACarrier()
    {
        PlaybackGrantStore store = Store(out _);

        store.Open(Unguessable.Issue(), Watcher, Seven);

        Assert.Null(store.Admit("not-a-ticket", Seven));
        Assert.Null(store.Admit(null, Seven));
        Assert.Null(store.Admit(Unguessable.Issue(), Seven));
    }

    [Fact]
    public void AGrantStopsAdmittingWhenItsTwoHoursAreUp()
    {
        PlaybackGrantStore store = Store(out WoundClock clock);
        string carrier = Unguessable.Issue();

        store.Open(carrier, Watcher, Seven);
        clock.Wind(PlaybackGrantPolicy.Default.Lifetime - TimeSpan.FromSeconds(1));

        Assert.Equal(Watcher, store.Admit(carrier, Seven));

        clock.Wind(TimeSpan.FromSeconds(1));

        Assert.Null(store.Admit(carrier, Seven));
    }

    [Fact]
    public void AGrantThatHasLapsedIsNotKeptAround()
    {
        PlaybackGrantStore store = Store(out WoundClock clock);

        store.Open(Unguessable.Issue(), Watcher, Seven);
        clock.Wind(PlaybackGrantPolicy.Default.Lifetime);
        store.Admit(Unguessable.Issue(), Seven);

        Assert.Equal(1, store.Count);

        store.Open(Unguessable.Issue(), Watcher, Eight);

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void TakingAWatchersGrantsBackClosesEveryOneOfThemAtOnce()
    {
        PlaybackGrantStore store = Store(out _);
        string mine = Unguessable.Issue();
        string another = Unguessable.Issue();
        string theirs = Unguessable.Issue();

        store.Open(mine, Watcher, Seven);
        store.Open(another, Watcher, Eight);
        store.Open(theirs, Somebody, Seven);

        Assert.Equal(2, store.RevokeEverythingOf(Watcher));
        Assert.Null(store.Admit(mine, Seven));
        Assert.Null(store.Admit(another, Eight));
        Assert.Equal(Somebody, store.Admit(theirs, Seven));
    }

    [Fact]
    public void AWatcherHoldsNoMoreGrantsThanTheCeilingAndTheOldestGoesFirst()
    {
        PlaybackGrantStore store = Store(out WoundClock clock);
        List<string> carriers = [];

        for (int opened = 0; opened <= PlaybackGrantStore.MostHeldPerSubject; opened++)
        {
            string carrier = Unguessable.Issue();

            carriers.Add(carrier);
            store.Open(carrier, Watcher, PlaybackTarget.Recording(opened.ToString(Culture)));
            clock.Wind(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(PlaybackGrantStore.MostHeldPerSubject, store.Count);
        Assert.Null(store.Admit(carriers[0], PlaybackTarget.Recording("0")));
        Assert.Equal(
            Watcher,
            store.Admit(carriers[^1], PlaybackTarget.Recording(PlaybackGrantStore.MostHeldPerSubject.ToString(Culture))));
    }

    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;

    private static PlaybackGrantStore Store(out WoundClock clock)
    {
        clock = new WoundClock(At);

        return new PlaybackGrantStore(clock, PlaybackGrantPolicy.Default);
    }
}
