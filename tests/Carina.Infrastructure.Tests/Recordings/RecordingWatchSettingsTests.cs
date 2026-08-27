using Carina.Infrastructure.Recordings;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingWatchSettingsTests
{
    [Fact]
    public void TheOnesTheWatchRunsWithAreTheOnesItWasGiven()
    {
        var settings = new RecordingWatchSettings(
            TimeSpan.FromSeconds(7),
            TimeSpan.FromSeconds(11),
            4,
            TimeSpan.FromSeconds(3),
            2);

        Assert.Equal(TimeSpan.FromSeconds(7), settings.BeforeFirstWatch);
        Assert.Equal(TimeSpan.FromSeconds(11), settings.BetweenWatches);
        Assert.Equal(4, settings.AttemptsAtReopening);
        Assert.Equal(TimeSpan.FromSeconds(3), settings.BetweenReopenings);
        Assert.Equal(2, settings.AttemptsAtACollision);
    }

    [Fact]
    public void TheOnesTheWatchRunsWithByDefaultAreTheOnesTheRulesName()
    {
        Assert.Equal(5, RecordingWatchSettings.Default.AttemptsAtReopening);
        Assert.Equal(TimeSpan.FromSeconds(2), RecordingWatchSettings.Default.BetweenReopenings);
        Assert.True(RecordingWatchSettings.Default.AttemptsAtACollision > 1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AWaitBeforeTheFirstPassIsLongerThanNothing(int ticks)
        => Refuses("beforeFirstWatch", () => Settings(beforeFirstWatch: TimeSpan.FromTicks(ticks)));

    [Fact]
    public void AWaitBeforeTheFirstPassOfOneTickIsEnough()
        => Assert.Equal(
            TimeSpan.FromTicks(1),
            Settings(beforeFirstWatch: TimeSpan.FromTicks(1)).BeforeFirstWatch);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AGapBetweenPassesIsLongerThanNothing(int ticks)
        => Refuses("betweenWatches", () => Settings(betweenWatches: TimeSpan.FromTicks(ticks)));

    [Fact]
    public void AGapBetweenPassesOfOneTickIsEnough()
        => Assert.Equal(TimeSpan.FromTicks(1), Settings(betweenWatches: TimeSpan.FromTicks(1)).BetweenWatches);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AStreamIsOpenedAgainAtLeastOnce(int attempts)
        => Refuses("attemptsAtReopening", () => Settings(attemptsAtReopening: attempts));

    [Fact]
    public void OneAttemptAtOpeningTheStreamAgainIsEnough()
        => Assert.Equal(1, Settings(attemptsAtReopening: 1).AttemptsAtReopening);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void APauseBetweenAttemptsIsLongerThanNothing(int ticks)
        => Refuses("betweenReopenings", () => Settings(betweenReopenings: TimeSpan.FromTicks(ticks)));

    [Fact]
    public void APauseBetweenAttemptsOfOneTickIsEnough()
        => Assert.Equal(TimeSpan.FromTicks(1), Settings(betweenReopenings: TimeSpan.FromTicks(1)).BetweenReopenings);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AWriteIsAttemptedAtLeastOnce(int attempts)
        => Refuses("attemptsAtACollision", () => Settings(attemptsAtACollision: attempts));

    [Fact]
    public void OneAttemptAtAWriteIsEnough()
        => Assert.Equal(1, Settings(attemptsAtACollision: 1).AttemptsAtACollision);

    private static void Refuses(string parameterName, Func<RecordingWatchSettings> making)
        => Assert.Equal(
            parameterName,
            Assert.Throws<ArgumentOutOfRangeException>(() => making()).ParamName);

    private static RecordingWatchSettings Settings(
        TimeSpan? beforeFirstWatch = null,
        TimeSpan? betweenWatches = null,
        int attemptsAtReopening = 5,
        TimeSpan? betweenReopenings = null,
        int attemptsAtACollision = 3)
        => new(
            beforeFirstWatch ?? TimeSpan.FromSeconds(10),
            betweenWatches ?? TimeSpan.FromSeconds(10),
            attemptsAtReopening,
            betweenReopenings ?? TimeSpan.FromSeconds(2),
            attemptsAtACollision);
}
