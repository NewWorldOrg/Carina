using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class TranscodeCeilingTests
{
    [Fact]
    public void ACeilingSaysHowManyAreRunningAndHowManyTheMachineIsAskedToRun()
    {
        TranscodeCeiling ceiling = new(3, 3);

        Assert.Equal(3, ceiling.Running);
        Assert.Equal(3, ceiling.AtOnce);
        Assert.Contains("3 transcoder", ceiling.Said, StringComparison.Ordinal);
        Assert.Contains("live and playback together", ceiling.Said, StringComparison.Ordinal);
    }

    [Fact]
    public void ACeilingIsOnlyReachedWhenAsManyAreRunningAsTheMachineIsAskedTo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeCeiling(1, -1));
        Assert.Equal(4, new TranscodeCeiling(4, 3).Running);
    }
}
