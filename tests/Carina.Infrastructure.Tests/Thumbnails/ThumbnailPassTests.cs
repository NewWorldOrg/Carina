using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class ThumbnailPassTests
{
    [Fact]
    public void APassSaysWhatItDidAndWhatItLeftBehind()
    {
        ThumbnailPass pass = ThumbnailPass.Of(read: 8, drawn: 3, skipped: 2, failed: 1, outOfReach: 40);

        Assert.False(pass.AlreadyRunning);
        Assert.False(pass.NowhereToPutThem);
        Assert.Equal((8, 3, 2, 1, 40), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
        Assert.Equal(2, pass.LeftForNextTime);
    }

    [Fact]
    public void APassThatSettledEverythingItReadLeavesNothingBehind()
        => Assert.Equal(0, ThumbnailPass.Of(3, 1, 1, 1, 0).LeftForNextTime);

    [Theory]
    [InlineData(-1, 0, 0, 0, 0, "read")]
    [InlineData(0, -1, 0, 0, 0, "drawn")]
    [InlineData(0, 0, -1, 0, 0, "skipped")]
    [InlineData(0, 0, 0, -1, 0, "failed")]
    [InlineData(0, 0, 0, 0, -1, "outOfReach")]
    public void APassCannotHaveDoneLessThanNoneOfAnything(
        int read,
        int drawn,
        int skipped,
        int failed,
        int outOfReach,
        string named)
        => Assert.Equal(
            named,
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThumbnailPass.Of(read, drawn, skipped, failed, outOfReach)).ParamName);

    [Theory]
    [InlineData(2, 3, 0, 0)]
    [InlineData(2, 0, 3, 0)]
    [InlineData(2, 0, 0, 3)]
    [InlineData(2, 1, 1, 1)]
    public void APassCannotHaveSettledMoreThanItRead(int read, int drawn, int skipped, int failed)
        => Assert.Equal(
            "read",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThumbnailPass.Of(read, drawn, skipped, failed, 0)).ParamName);

    [Fact]
    public void SettlingExactlyWhatWasReadIsAccepted()
        => Assert.Equal(0, ThumbnailPass.Of(3, 1, 1, 1, 0).LeftForNextTime);

    [Fact]
    public void APassThatWasRefusedCountedNothing()
    {
        Assert.True(ThumbnailPass.RefusedBecauseOneIsRunning().AlreadyRunning);
        Assert.False(ThumbnailPass.RefusedBecauseOneIsRunning().NowhereToPutThem);
        Assert.True(ThumbnailPass.RefusedBecauseThereIsNowhereToPutThem().NowhereToPutThem);
        Assert.False(ThumbnailPass.RefusedBecauseThereIsNowhereToPutThem().AlreadyRunning);
        Assert.Equal(0, ThumbnailPass.RefusedBecauseOneIsRunning().Read);
        Assert.Equal(0, ThumbnailPass.RefusedBecauseThereIsNowhereToPutThem().OutOfReach);
    }
}
