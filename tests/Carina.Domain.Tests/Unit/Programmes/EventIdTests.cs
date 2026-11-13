using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Unit.Programmes;

[Trait("Category", "Unit")]
public sealed class EventIdTests
{
    [Theory]
    [InlineData(EventId.MinValue)]
    [InlineData(EventId.MaxValue)]
    [InlineData(30000)]
    public void AnEventIdTheBroadcastCanUseIsAccepted(int value)
        => Assert.Equal(value, new EventId(value).Value);

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0xFFFF)]
    [InlineData(-1)]
    [InlineData(0x10000)]
    public void AnEventIdNoBroadcastNamesAProgrammeWithIsRefused(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EventId(value));
}
