using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class GuideWindowTests
{
    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ADayIsAskedForAndGiven()
    {
        GuideWindow? window = GuideWindow.Between(From, From.AddDays(1));

        Assert.NotNull(window);
        Assert.Equal(From, window.From);
    }

    [Fact]
    public void TwoDaysIsTheMostThatMayBeAskedFor()
        => Assert.NotNull(GuideWindow.Between(From, From + GuideWindow.Longest));

    [Fact]
    public void AMomentBeyondTwoDaysIsRefused()
        => Assert.Null(GuideWindow.Between(From, From + GuideWindow.Longest + TimeSpan.FromSeconds(1)));

    [Fact]
    public void AWindowThatEndsWhereItStartsCarriesNothingAndIsRefused()
        => Assert.Null(GuideWindow.Between(From, From));

    [Fact]
    public void AWindowThatRunsBackwardsIsRefused()
        => Assert.Null(GuideWindow.Between(From, From.AddHours(-1)));

    [Fact]
    public void AMomentWithoutAnOffsetIsRefusedRatherThanGuessedAt()
        => Assert.Null(GuideWindow.Between(
            new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Unspecified),
            From.AddDays(1)));
}
