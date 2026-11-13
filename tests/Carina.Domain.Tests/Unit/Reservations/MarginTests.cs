using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class MarginTests
{
    [Fact]
    public void AMarginIsAWholeNumberOfSecondsWithinAnHour()
    {
        Assert.Equal(30, Margin.OfSeconds(30).Seconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => Margin.OfSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Margin(Margin.Longest + TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new Margin(TimeSpan.FromMilliseconds(1500)));
    }
}
