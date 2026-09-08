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
        Assert.True(settings.SaysSomethingBeforeTheCeiling);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveWireSettings { SilenceCeiling = TimeSpan.Zero });
    }

    [Fact]
    public void APingIntervalThatWouldReachThatCeilingIsSeenForWhatItIs()
    {
        LiveWireSettings settings = new()
        {
            BetweenPings = TimeSpan.FromSeconds(120),
            SilenceCeiling = TimeSpan.FromSeconds(100),
        };

        Assert.False(settings.SaysSomethingBeforeTheCeiling);
    }
}
