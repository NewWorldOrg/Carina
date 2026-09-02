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
