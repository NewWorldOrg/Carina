using System.Reflection;

using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class ConstantQuantiserTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    public void AQuantiserOutsideWhatTheCodecQuantisesIsRefused(int quantiser)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantQuantiser(quantiser));
    }

    [Theory]
    [InlineData(ConstantQuantiser.Finest)]
    [InlineData(24)]
    [InlineData(ConstantQuantiser.Coarsest)]
    public void AQuantiserOnTheEdgeOfWhatTheCodecQuantisesIsTaken(int quantiser)
    {
        Assert.Equal(quantiser, new ConstantQuantiser(quantiser).Quantiser);
    }

    [Fact]
    public void AQuantiserCarriesAQuantiserAndNothingElse()
    {
        Assert.Equal(
            [nameof(ConstantQuantiser.Quantiser)],
            typeof(ConstantQuantiser)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name));
    }
}
