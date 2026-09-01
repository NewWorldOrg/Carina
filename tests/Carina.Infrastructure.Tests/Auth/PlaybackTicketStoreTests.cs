using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class PlaybackTicketStoreTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

    [Fact]
    public void WhatIsHandedBackSaysWhenTheTicketDies()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Equal(At + PlaybackTicketPolicy.Default.Lifetime, issued.LapsesAt);
    }

    [Fact]
    public void ATicketOpensWhatItWasIssuedFor()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Equal(Watcher, store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketIsSpentWhenItIsUsedAndTheSecondUseIsRefused()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.NotNull(store.Spend(issued.InTheClear, Seven));
        Assert.Null(store.Spend(issued.InTheClear, Seven));
        Assert.Null(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketForOneRecordingDoesNotOpenAnother()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Null(store.Spend(issued.InTheClear, Eight));
    }

    [Fact]
    public void ATicketForARecordingDoesNotOpenTheLiveChannelOfTheSameName()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Null(store.Spend(issued.InTheClear, PlaybackTarget.LiveChannel("7")));
    }

    [Fact]
    public void OfferingATicketAtTheWrongDoorSpendsItSoNobodyCanAskItWhichDoorItOpens()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Null(store.Spend(issued.InTheClear, Eight));
        Assert.Null(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketStillOpensItsTargetASecondBeforeItLapses()
    {
        PlaybackTicketStore store = Store(out WoundClock clock);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime - TimeSpan.FromSeconds(1));

        Assert.NotNull(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketOpensNothingTheMomentItLapses()
    {
        PlaybackTicketStore store = Store(out WoundClock clock);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);

        Assert.Null(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketOpensNothingASecondAfterItLapses()
    {
        PlaybackTicketStore store = Store(out WoundClock clock);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void ATicketNobodyIssuedOpensNothing()
    {
        PlaybackTicketStore store = Store(out _);

        Assert.Null(store.Spend(Unguessable.Issue(), Seven));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-ticket")]
    [InlineData("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    [InlineData("../../etc/passwd")]
    public void ATicketThatIsNotTheShapeOfOneOpensNothing(string offered)
    {
        PlaybackTicketStore store = Store(out _);

        Issued(store, Watcher, Seven);

        Assert.Null(store.Spend(offered, Seven));
    }

    [Fact]
    public void NoTicketAtAllOpensNothing()
    {
        PlaybackTicketStore store = Store(out _);

        Assert.Null(store.Spend(null, Seven));
    }

    [Fact]
    public void ATicketOfTheRightLengthThatDiffersByOneCharacterOpensNothing()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);
        char first = issued.InTheClear[0];
        string nearly = (first == 'a' ? 'b' : 'a') + issued.InTheClear[1..];

        Assert.Null(store.Spend(nearly, Seven));
        Assert.NotNull(store.Spend(issued.InTheClear, Seven));
    }

    [Fact]
    public void OnlyOneOfManyCallersRacingForTheSameTicketGetsIn()
    {
        const int Racers = 16;
        const int Rounds = 200;

        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket[] tickets =
        [
            .. Enumerable.Range(0, Rounds).Select(round => Issued(store, new Subject($"racer-{round}"), Seven)),
        ];
        int[] admitted = new int[Rounds];

        using var gun = new Barrier(Racers);
        Thread[] racers =
        [
            .. Enumerable.Range(0, Racers).Select(_ => new Thread(() =>
            {
                for (int round = 0; round < Rounds; round++)
                {
                    gun.SignalAndWait();

                    if (store.Spend(tickets[round].InTheClear, Seven) is not null)
                    {
                        Interlocked.Increment(ref admitted[round]);
                    }
                }
            })),
        ];

        foreach (Thread racer in racers)
        {
            racer.Start();
        }

        foreach (Thread racer in racers)
        {
            Assert.True(racer.Join(TimeSpan.FromMinutes(1)));
        }

        Assert.Equal(Enumerable.Repeat(1, Rounds), admitted);
    }

    [Fact]
    public void ATicketNobodyCollectedIsLetGoRatherThanKeptForever()
    {
        PlaybackTicketStore store = Store(out WoundClock clock);
        IssuedPlaybackTicket stale = Issued(store, Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);
        Issued(store, Watcher, Eight);

        Assert.Equal(1, store.Count);
        Assert.Null(store.Spend(stale.InTheClear, Seven));
    }

    [Fact]
    public void AnAccountHoldsOnlyAHandfulOfLiveTicketsAtOnce()
    {
        PlaybackTicketStore store = Store(out _);

        for (int asked = 0; asked < PlaybackTicketStore.MostHeldPerSubject; asked++)
        {
            Assert.NotNull(store.Issue(Watcher, Seven));
        }

        Assert.Null(store.Issue(Watcher, Seven));
        Assert.Equal(PlaybackTicketStore.MostHeldPerSubject, store.Count);
    }

    [Fact]
    public void AnAccountThatSpendsWhatItAsksForCanGoOnAsking()
    {
        PlaybackTicketStore store = Store(out _);

        for (int asked = 0; asked < PlaybackTicketStore.MostHeldPerSubject * 4; asked++)
        {
            Assert.NotNull(store.Spend(Issued(store, Watcher, Seven).InTheClear, Seven));
        }
    }

    [Fact]
    public void TicketsNobodyCollectedMakeRoomForTheNextOnesOnceTheyLapse()
    {
        PlaybackTicketStore store = Store(out WoundClock clock);

        for (int asked = 0; asked < PlaybackTicketStore.MostHeldPerSubject; asked++)
        {
            Assert.NotNull(store.Issue(Watcher, Seven));
        }

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);

        Assert.NotNull(store.Issue(Watcher, Seven));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void AFloodOfRequestsCannotVoidATicketSomebodyElseIsAboutToUse()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket mine = Issued(store, Watcher, Seven);

        for (int flooded = 0; flooded < PlaybackTicketStore.MostHeldAtOnce * 2; flooded++)
        {
            store.Issue(new Subject($"flooder-{flooded}"), Eight);
        }

        Assert.Equal(Watcher, store.Spend(mine.InTheClear, Seven));
    }

    [Fact]
    public void TheStoreStopsIssuingRatherThanGrowWithoutBound()
    {
        PlaybackTicketStore store = Store(out _);

        for (int flooded = 0; flooded < PlaybackTicketStore.MostHeldAtOnce * 2; flooded++)
        {
            store.Issue(new Subject($"flooder-{flooded}"), Seven);
        }

        Assert.Equal(PlaybackTicketStore.MostHeldAtOnce, store.Count);
        Assert.Null(store.Issue(new Subject("one-more"), Seven));
    }

    [Fact]
    public void TheStoreIsAskedForATargetBeforeItWillSpendAnything()
    {
        PlaybackTicketStore store = Store(out _);
        IssuedPlaybackTicket issued = Issued(store, Watcher, Seven);

        Assert.Throws<ArgumentNullException>(() => store.Spend(issued.InTheClear, null!));
    }

    private static IssuedPlaybackTicket Issued(PlaybackTicketStore store, Subject subject, PlaybackTarget target)
    {
        IssuedPlaybackTicket? issued = store.Issue(subject, target);

        Assert.NotNull(issued);

        return issued;
    }

    private static PlaybackTicketStore Store(out WoundClock clock)
    {
        clock = new WoundClock(At);

        return new PlaybackTicketStore(clock, PlaybackTicketPolicy.Default);
    }
}
