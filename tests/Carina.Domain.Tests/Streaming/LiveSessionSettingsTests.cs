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
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(linger: TimeSpan.Zero));
    }

    [Fact]
    public void ALingerOfLessThanNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(linger: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void AnyPositiveLingerIsKept()
    {
        Assert.Equal(TimeSpan.FromSeconds(12), new LiveSessionSettings(linger: TimeSpan.FromSeconds(12)).Linger);
    }

    [Fact]
    public void ARaiseOfNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(longestRaise: TimeSpan.Zero));
    }

    [Fact]
    public void GivingATranscoderNoTimeAtAllToTakeAMouthfulIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(longestWaitToBeFed: TimeSpan.Zero));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void HoldingNoBytesAtAllForATranscoderIsRefused(long bytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(mostBytesWaitingToBeFed: bytes));
    }

    [Fact]
    public void ATranscoderIsHeldThirtyTwoMebibytesItHasNotTakenByDefault()
    {
        Assert.Equal(32L * 1024 * 1024, new LiveSessionSettings().MostBytesWaitingToBeFed);
    }

    [Fact]
    public void BeingWatchedHoldsTheSupplyTenMinutesAheadAndSaysSoEveryMinute()
    {
        LiveSessionSettings settings = new();

        Assert.Equal(TimeSpan.FromMinutes(10), settings.HeldAhead);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.BetweenHolds);
    }

    [Fact]
    public void TheRestOfWhatASessionIsGivenByDefaultIsWhatItAlwaysWas()
    {
        LiveSessionSettings settings = new();

        Assert.Equal(TimeSpan.FromSeconds(30), settings.LongestRaise);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.LongestWaitToBeFed);
    }

    [Fact]
    public void AskingLessOftenThanWhatIsAskedForLastsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(
            heldAhead: TimeSpan.FromMinutes(2),
            betweenHolds: TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void AskingExactlyAsOftenAsWhatIsAskedForLastsIsRefusedToo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(
            heldAhead: TimeSpan.FromMinutes(2),
            betweenHolds: TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void AskingMoreOftenThanWhatIsAskedForLastsIsTakenAsGiven()
    {
        LiveSessionSettings settings = new(
            heldAhead: TimeSpan.FromMinutes(2),
            betweenHolds: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromMinutes(2), settings.HeldAhead);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.BetweenHolds);
    }

    [Fact]
    public void HoldingTheSupplyOpenForNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(heldAhead: TimeSpan.Zero));
    }

    [Fact]
    public void AskingToHoldItOpenNeverIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveSessionSettings(betweenHolds: TimeSpan.Zero));
    }

    [Fact]
    public void ByDefaultAViewerWaitsFiveSecondsForATunerAlreadyOnItsWayOut()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), new LiveSessionSettings().LongestWaitForATunerToComeFree);
    }

    [Fact]
    public void WaitingNoTimeAtAllForATunerOnItsWayOutIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LiveSessionSettings(longestWaitForATunerToComeFree: TimeSpan.Zero));
    }

    [Fact]
    public void AnyPositiveWaitForATunerOnItsWayOutIsKept()
    {
        LiveSessionSettings settings = new(longestWaitForATunerToComeFree: TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), settings.LongestWaitForATunerToComeFree);
    }
}
