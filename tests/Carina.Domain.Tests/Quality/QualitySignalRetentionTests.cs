using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySignalRetentionTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a raw sample nothing has rolled up yet is not deleted")]
    public void ARawSampleNothingHasRolledUpYetIsNotDeleted()
        => Assert.Null(QualitySignalRetention.SamplesTakenBefore(
            Now,
            TimeSpan.FromDays(7),
            new Dictionary<QualityWindow, DateTime?>
            {
                [QualityWindow.Minute] = Now,
                [QualityWindow.Hour] = null,
            }));

    [Fact(DisplayName = "nothing is deleted while no window at all has been written")]
    public void NothingIsDeletedWhileNoWindowAtAllHasBeenWritten()
        => Assert.Null(QualitySignalRetention.SamplesTakenBefore(
            Now,
            TimeSpan.FromDays(7),
            new Dictionary<QualityWindow, DateTime?>()));

    [Fact(DisplayName = "a sample past its retention goes once every window holding it is written")]
    public void ASamplePastItsRetentionGoesOnceEveryWindowHoldingItIsWritten()
        => Assert.Equal(
            Now.AddDays(-7),
            QualitySignalRetention.SamplesTakenBefore(
                Now,
                TimeSpan.FromDays(7),
                new Dictionary<QualityWindow, DateTime?>
                {
                    [QualityWindow.Minute] = Now,
                    [QualityWindow.Hour] = Now,
                }));

    [Fact(DisplayName = "a rollup that has fallen behind holds the sweep back to where it reached")]
    public void ARollupThatHasFallenBehindHoldsTheSweepBackToWhereItReached()
        => Assert.Equal(
            Now.AddDays(-9),
            QualitySignalRetention.SamplesTakenBefore(
                Now,
                TimeSpan.FromDays(7),
                new Dictionary<QualityWindow, DateTime?>
                {
                    [QualityWindow.Minute] = Now.AddDays(-1),
                    [QualityWindow.Hour] = Now.AddDays(-9),
                }));

    [Fact]
    public void ARetentionOfNoTimeAtAllIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualitySignalRetention.SamplesTakenBefore(
            Now,
            TimeSpan.Zero,
            new Dictionary<QualityWindow, DateTime?>()));

    [Fact(DisplayName = "the hourly windows are kept for as long as there is a system")]
    public void TheHourlyWindowsAreKeptForAsLongAsThereIsASystem()
        => Assert.Null(QualitySignalRetention.WindowsStartedBefore(Now, null));

    [Fact(DisplayName = "a window layer with a retention is swept by it")]
    public void AWindowLayerWithARetentionIsSweptByIt()
        => Assert.Equal(Now.AddDays(-90), QualitySignalRetention.WindowsStartedBefore(Now, TimeSpan.FromDays(90)));
}
