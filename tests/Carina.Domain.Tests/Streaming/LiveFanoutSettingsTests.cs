using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveFanoutSettingsTests
{
    [Fact]
    public void ByDefaultAViewerMayFallFifteenFragmentsBehindBeforeAnythingIsThrownAway()
    {
        Assert.Equal(15, new LiveFanoutSettings().LongestBacklog);
    }

    [Fact]
    public void ABacklogOfNothingWouldThrowEveryPictureAway()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveFanoutSettings { LongestBacklog = 0 });
    }

    [Fact]
    public void ABacklogOfLessThanNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveFanoutSettings { LongestBacklog = -3 });
    }

    [Fact]
    public void ABacklogOfOneIsTheShortestThereIs()
    {
        Assert.Equal(1, new LiveFanoutSettings { LongestBacklog = 1 }.LongestBacklog);
    }
}
