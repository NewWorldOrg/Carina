using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class NptReferenceTests
{
    [Theory]
    [InlineData(-1, 0, 1, 1)]
    [InlineData(0x100, 0, 1, 1)]
    [InlineData(0x40, -1, 1, 1)]
    [InlineData(0x40, 1L << 33, 1, 1)]
    [InlineData(0x40, 0, 0, 1)]
    [InlineData(0x40, 0, 1, 0)]
    [InlineData(0x40, 0, 0x10000, 1)]
    public void AReferenceOfTheProgrammeTimeOutsideItsFieldsIsRefused(int tag, long npt, int numerator, int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NptReference(tag, 0, npt, numerator, denominator));
    }
}
