using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeProgressTests
{
    private static readonly TimeSpan Whole = TimeSpan.FromSeconds(2097.502489);

    [Fact(DisplayName = "BR-ED2-013: how far along a job is comes from a whole read off the source")]
    public void HowFarAlongAJobIsComesFromAWholeReadOffTheSource()
    {
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.FromSeconds(1048.75), Whole, speed: 2, ended: false);

        Assert.Equal(0.5, progress.Portion!.Value, 3);
        Assert.Equal(524.376, progress.Left!.Value.TotalSeconds, 3);
    }

    [Fact(DisplayName = "BR-ED2-014: a job whose whole is unknown says how far it has got and not how far along")]
    public void AJobWhoseWholeIsUnknownSaysHowFarItHasGotAndNotHowFarAlong()
    {
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.FromSeconds(60), null, speed: 2, ended: false);

        Assert.Equal(TimeSpan.FromSeconds(60), progress.Reached);
        Assert.Null(progress.Portion);
        Assert.Null(progress.Left);
    }

    [Fact]
    public void APieceOfWorkCannotBeMoreThanAllOfIt()
    {
        EncodeProgress progress = EncodeProgress.Of(Whole + TimeSpan.FromSeconds(4), Whole, speed: 1, ended: false);

        Assert.Equal(1, progress.Portion);
        Assert.Equal(TimeSpan.Zero, progress.Left);
    }

    [Fact]
    public void AJobThatHasEndedIsAllOfTheWayThroughWithNothingLeft()
    {
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.FromSeconds(10), Whole, speed: 3, ended: true);

        Assert.True(progress.Ended);
        Assert.Equal(1, progress.Portion);
        Assert.Equal(TimeSpan.Zero, progress.Left);
    }

    [Fact]
    public void AJobGoingNowhereHasNoTimeLeftToGive()
    {
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.FromSeconds(10), Whole, speed: 0, ended: false);

        Assert.Null(progress.Left);
        Assert.NotNull(progress.Portion);
    }

    [Fact]
    public void AJobThatHasReachedNowhereYetIsAtTheStart()
    {
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.Zero, Whole, speed: 0, ended: false);

        Assert.Equal(0, progress.Portion);
        Assert.Equal(TimeSpan.Zero, progress.Reached);
    }

    [Fact]
    public void AJobCannotHaveReachedBeforeItStarted()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodeProgress.Of(TimeSpan.FromSeconds(-1), Whole, speed: 1, ended: false));

    [Fact]
    public void AWholeOfNoLengthIsNotAWhole()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodeProgress.Of(TimeSpan.Zero, TimeSpan.Zero, speed: 1, ended: false));

    [Fact]
    public void AJobCannotBeGoingBackwards()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodeProgress.Of(TimeSpan.Zero, Whole, speed: -1, ended: false));
}
