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
}
