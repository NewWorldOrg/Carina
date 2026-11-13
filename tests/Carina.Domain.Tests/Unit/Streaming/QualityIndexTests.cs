using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class QualityIndexTests
{
    [Theory(DisplayName = "an AV1 quality index outside what the card quantises between is refused")]
    [InlineData(-1)]
    [InlineData(256)]
    public void AnAv1QualityIndexOutsideWhatTheCardQuantisesBetweenIsRefused(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QualityIndex(index));
    }

    [Theory(DisplayName = "an AV1 quality index on the edge of what the card quantises between is taken")]
    [InlineData(QualityIndex.Finest)]
    [InlineData(80)]
    [InlineData(QualityIndex.Coarsest)]
    public void AnAv1QualityIndexOnTheEdgeIsTaken(int index)
    {
        Assert.Equal(index, new QualityIndex(index).Index);
    }
}
