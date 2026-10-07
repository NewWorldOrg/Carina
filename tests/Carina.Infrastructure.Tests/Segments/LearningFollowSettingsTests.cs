using Carina.Infrastructure.Segments;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class LearningFollowSettingsTests
{
    [Fact(DisplayName = "a follow that has caught up with a file still being written waits five seconds before reading on, so it is woken less often")]
    public void ACaughtUpFollowWaitsFiveSeconds()
        => Assert.Equal(TimeSpan.FromSeconds(5), LearningFollowSettings.Default.WhileCaughtUp);
}
