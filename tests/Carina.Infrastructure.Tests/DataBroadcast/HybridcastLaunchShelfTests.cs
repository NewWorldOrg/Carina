using Carina.Domain.Auth;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.DataBroadcast;

public sealed class HybridcastLaunchShelfTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly HybridcastLaunch Launch = new(
        "32736-1024",
        new Subject("viewer"),
        new Uri("http://hc.station.example.jp/p/index.html"),
        HybridcastBoundary.Of(new Uri("http://hc.station.example.jp/p/index.html"), []),
        "/hybridcast/receiver.js");

    [Fact(DisplayName = "BR-BA-004: a launch is found again by the ticket it was kept under, and by nothing else")]
    public void ALaunchIsFoundByItsTicket()
    {
        HybridcastLaunchShelf shelf = new(new HandTurnedClock(Noon), PlaybackGrantPolicy.Default);
        string carrier = Unguessable.Issue();

        shelf.Keep(carrier, Launch);

        Assert.Same(Launch, shelf.Find(carrier));
        Assert.Null(shelf.Find(Unguessable.Issue()));
        Assert.Null(shelf.Find("short"));
        Assert.Null(shelf.Find(null));
    }

    [Fact(DisplayName = "BR-BA-004: a launch lasts as long as the ticket and the grant it opens, and no longer")]
    public void ALaunchLastsAsLongAsItsGrant()
    {
        HandTurnedClock clock = new(Noon);
        HybridcastLaunchShelf shelf = new(clock, PlaybackGrantPolicy.Default);
        string carrier = Unguessable.Issue();
        shelf.Keep(carrier, Launch);

        clock.Turn(PlaybackTicketPolicy.LongestLifetime + PlaybackGrantPolicy.Default.Lifetime - TimeSpan.FromSeconds(1));
        Assert.NotNull(shelf.Find(carrier));

        clock.Turn(TimeSpan.FromSeconds(1));
        Assert.Null(shelf.Find(carrier));
    }

    [Fact(DisplayName = "BR-BA-004: the shelf holds a bounded number of launches and lets the oldest go first")]
    public void TheShelfLetsTheOldestGo()
    {
        HandTurnedClock clock = new(Noon);
        HybridcastLaunchShelf shelf = new(clock, PlaybackGrantPolicy.Default);
        string first = Unguessable.Issue();
        shelf.Keep(first, Launch);
        string[] later = [.. Enumerable.Range(0, HybridcastLaunchShelf.MostHeld).Select(_ => Unguessable.Issue())];

        foreach (string carrier in later)
        {
            clock.Turn(TimeSpan.FromSeconds(1));
            shelf.Keep(carrier, Launch);
        }

        Assert.Null(shelf.Find(first));
        Assert.All(later, carrier => Assert.NotNull(shelf.Find(carrier)));
    }
}
