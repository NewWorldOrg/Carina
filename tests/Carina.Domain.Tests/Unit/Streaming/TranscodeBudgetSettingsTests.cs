using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class TranscodeBudgetSettingsTests
{
    [Fact]
    public void TheMachineRunsFourTranscodersAtOnceUnlessItIsToldOtherwise()
    {
        Assert.Equal(4, new TranscodeBudgetSettings().AtOnce);
    }

    [Fact]
    public void ACeilingOfNoneIsRefusedBecauseItIsARouteThatNeverPlaysRatherThanABoundedOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeBudgetSettings { AtOnce = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscodeBudgetSettings { AtOnce = -1 });
        Assert.Equal(1, new TranscodeBudgetSettings { AtOnce = 1 }.AtOnce);
        Assert.Equal(TranscodeBudgetSettings.Fewest, new TranscodeBudgetSettings { AtOnce = 1 }.AtOnce);
    }
}
