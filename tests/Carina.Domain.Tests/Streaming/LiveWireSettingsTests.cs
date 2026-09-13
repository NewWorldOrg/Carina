using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveWireSettingsTests
{
    [Fact]
    public void TheSilenceCeilingIsTheHundredSecondsTheGatewayInFrontWaitsForTheFirstByte()
    {
        Assert.Equal(TimeSpan.FromSeconds(100), new LiveWireSettings().SilenceCeiling);
    }

    [Fact]
    public void TheWireSaysSomethingWellWithinThatCeiling()
    {
        LiveWireSettings settings = new();

        Assert.True(settings.BetweenPings < settings.SilenceCeiling);
    }

    [Fact]
    public void SixQuietsFitUnderTheDefaultCeilingSoTheSeventhIsPastIt()
    {
        LiveWireSettings settings = new();

        Assert.Equal(6, settings.QuietsBeforeTheCeiling);
        Assert.True(settings.BetweenPings * settings.QuietsBeforeTheCeiling <= settings.SilenceCeiling);
        Assert.True(settings.BetweenPings * (settings.QuietsBeforeTheCeiling + 1) > settings.SilenceCeiling);
    }

    [Fact]
    public void ACeilingOfNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(silenceCeiling: TimeSpan.Zero));
    }

    [Fact]
    public void APingIntervalOfNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(betweenPings: TimeSpan.Zero));
    }

    [Fact]
    public void AWritePatienceOfNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(writePatience: TimeSpan.Zero));
    }

    [Fact]
    public void NoRoomAtAllForWhatAViewerSaysIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(largestFrameFromAViewer: 0));
    }

    [Fact]
    public void APingIntervalThatWouldReachTheCeilingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(
            betweenPings: TimeSpan.FromSeconds(120),
            silenceCeiling: TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void APingIntervalThatOnlyMeetsTheCeilingIsRefusedToo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings(
            betweenPings: TimeSpan.FromSeconds(100),
            silenceCeiling: TimeSpan.FromSeconds(100)));
    }

    [Fact]
    public void APingIntervalUnderTheCeilingIsTakenAsGiven()
    {
        LiveWireSettings settings = new(
            betweenPings: TimeSpan.FromSeconds(30),
            silenceCeiling: TimeSpan.FromSeconds(200));

        Assert.Equal(TimeSpan.FromSeconds(30), settings.BetweenPings);
        Assert.Equal(TimeSpan.FromSeconds(200), settings.SilenceCeiling);
        Assert.Equal(6, settings.QuietsBeforeTheCeiling);
    }
}
