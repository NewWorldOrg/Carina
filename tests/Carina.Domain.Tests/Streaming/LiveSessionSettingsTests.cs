using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSessionSettingsTests
{
    [Fact]
    public void ByDefaultASessionOutlivesItsLastViewerByFiveSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new LiveSessionSettings().Linger);
    }

    [Fact]
    public void ALingerOfNothingWouldTearDownOnEveryReload()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings { Linger = TimeSpan.Zero });
    }

    [Fact]
    public void ALingerOfLessThanNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings { Linger = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void AnyPositiveLingerIsKept()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), new LiveSessionSettings { Linger = TimeSpan.FromSeconds(12) }.Linger);
    }

    [Fact]
    public void BeingWatchedHoldsTheSupplyTenMinutesAheadAndSaysSoEveryMinute()
    {
        LiveSessionSettings settings = new();

        Assert.Equal(TimeSpan.FromMinutes(10), settings.HeldAhead);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.BetweenHolds);
    }

    [Fact]
    public void TheSupplyIsAskedAgainWellBeforeWhatWasAskedForRunsOut()
    {
        Assert.True(new LiveSessionSettings().AsksBeforeWhatItAskedForRunsOut);
    }

    [Fact]
    public void AskingLessOftenThanWhatIsAskedForLastsIsSeenForWhatItIs()
    {
        LiveSessionSettings settings = new()
        {
            HeldAhead = TimeSpan.FromMinutes(2),
            BetweenHolds = TimeSpan.FromMinutes(5),
        };

        Assert.False(settings.AsksBeforeWhatItAskedForRunsOut);
    }

    [Fact]
    public void HoldingTheSupplyOpenForNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings { HeldAhead = TimeSpan.Zero });
    }

    [Fact]
    public void AskingToHoldItOpenNeverIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings { BetweenHolds = TimeSpan.Zero });
    }
}
