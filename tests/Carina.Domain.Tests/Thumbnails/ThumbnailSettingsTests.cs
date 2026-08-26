using Carina.Domain.Thumbnails;

namespace Carina.Domain.Tests.Thumbnails;

public sealed class ThumbnailSettingsTests
{
    private static readonly ThumbnailSettings Unset = new();

    [Fact]
    public void ThePositionIsTakenTwoMinutesInOrAThirdOfTheWayThroughWhicheverComesFirst()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), Unset.NoLaterThan);
        Assert.Equal(3, Unset.OneOverAShareOf);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(357, 119)]
    [InlineData(360, 120)]
    [InlineData(363, 120)]
    [InlineData(7200, 120)]
    public void APictureIsTakenAThirdOfTheWayInUntilThatWouldBePastTheCap(int written, int expected)
        => Assert.Equal(
            TimeSpan.FromSeconds(expected),
            Unset.PositionIn(TimeSpan.FromSeconds(written)));

    [Fact]
    public void TheCapIsWhatDecidesOnceAThirdOfTheWayIsFurtherIn()
    {
        Assert.Equal(TimeSpan.FromSeconds(119.999), Unset.PositionIn(TimeSpan.FromSeconds(359.997)));
        Assert.Equal(TimeSpan.FromSeconds(120), Unset.PositionIn(TimeSpan.FromSeconds(360.003)));
    }

    [Fact]
    public void BothHalvesOfTheRuleCanBeConfigured()
    {
        var closer = new ThumbnailSettings { NoLaterThan = TimeSpan.FromSeconds(20), OneOverAShareOf = 2 };

        Assert.Equal(TimeSpan.FromSeconds(15), closer.PositionIn(TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(20), closer.PositionIn(TimeSpan.FromSeconds(3600)));
        Assert.Equal(TimeSpan.FromSeconds(120), Unset.PositionIn(TimeSpan.FromSeconds(3600)));
    }

    [Fact]
    public void ARecordingShorterThanNothingIsRefused()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => Unset.PositionIn(TimeSpan.FromMilliseconds(-1)));

        Assert.Equal("written", refusal.ParamName);
    }

    [Fact]
    public void NoDirectoryMeansNoPictures()
    {
        Assert.False(Unset.DrawsAnything);
        Assert.True((Unset with { WrittenTo = "/srv/thumbnails" }).DrawsAnything);
    }
}
