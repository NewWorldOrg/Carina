using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class RollingHorizonTests
{
    [Fact]
    public void TheProvisionalHorizonIsHalfAnHour()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), RollingHorizon.Provisional);
        Assert.Equal(RollingHorizon.Provisional, RollingHorizon.Default.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AHorizonReachesForwards(int seconds)
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RollingHorizon(TimeSpan.FromSeconds(seconds)));

        Assert.Equal("value", refused.ParamName);
    }

    [Fact]
    public void OneSecondIsAHorizon()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new RollingHorizon(TimeSpan.FromSeconds(1)).Value);
    }

    [Fact]
    public void AHorizonIsAWholeNumberOfSeconds()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new RollingHorizon(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1)));

        Assert.Equal("value", refused.ParamName);
    }
}
