using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class PriorityTests
{
    [Theory]
    [InlineData(Priority.MinValue - 1)]
    [InlineData(Priority.MaxValue + 1)]
    public void APriorityOutsideItsRangeIsRefused(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Priority(value));

    [Theory]
    [InlineData(Priority.MinValue)]
    [InlineData(Priority.DefaultValue)]
    [InlineData(Priority.MaxValue)]
    public void APriorityInsideItsRangeIsKept(int value)
        => Assert.Equal(value, new Priority(value).Value);
}
