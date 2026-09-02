using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveBacklogTests
{
    [Fact]
    public void ABacklogSaysHowManyAreWaitingAndHowManyWereThrownAway()
    {
        LiveBacklog backlog = new(3, 17L);

        Assert.Equal(3, backlog.Queued);
        Assert.Equal(17L, backlog.Dropped);
    }

    [Fact]
    public void NothingWaitingAndNothingThrownAwayIsTheEmptyBacklog()
    {
        Assert.Equal(0, LiveBacklog.Empty.Queued);
        Assert.Equal(0L, LiveBacklog.Empty.Dropped);
    }

    [Fact]
    public void FewerThanNoFramesCannotBeWaiting()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveBacklog(-1, 0L));
    }

    [Fact]
    public void FewerThanNoFramesCannotHaveBeenThrownAway()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveBacklog(0, -1L));
    }

    [Fact]
    public void TwoReadingsOfTheSameNumbersAreTheSameBacklog()
    {
        Assert.Equal(new LiveBacklog(2, 5L), new LiveBacklog(2, 5L));
    }
}
