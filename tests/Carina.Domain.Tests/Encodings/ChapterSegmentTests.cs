using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class ChapterSegmentTests
{
    [Fact]
    public void AChapterStartsNoEarlierThanTheArtefactDoes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChapterSegment(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(10), ChapterKind.Programme));
    }

    [Fact]
    public void AChapterEndsAfterItStartsAndNotAtTheSameMomentOrBefore()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChapterSegment(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), ChapterKind.Break));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChapterSegment(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(9), ChapterKind.Break));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void AChapterIsOneOfTheKindsNamedHereAndNothingElse(int kind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(10), (ChapterKind)kind));
    }

    [Fact]
    public void HowLongAChapterLastsIsHowFarItsEndIsFromItsStart()
    {
        var stretch = new ChapterSegment(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), ChapterKind.Break);

        Assert.Equal(TimeSpan.FromSeconds(60), stretch.Length);
        Assert.Equal(ChapterKind.Break, stretch.Kind);
    }
}
