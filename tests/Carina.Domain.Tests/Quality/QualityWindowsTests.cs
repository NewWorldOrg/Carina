using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityWindowsTests
{
    private static readonly DateTime Sometime = new(2026, 9, 8, 12, 34, 56, DateTimeKind.Utc);

    [Fact]
    public void AMinuteStartsAtTheMinuteAReadingFellIn()
        => Assert.Equal(
            new DateTime(2026, 9, 8, 12, 34, 0, DateTimeKind.Utc),
            QualityWindows.StartOf(Sometime, QualityWindow.Minute));

    [Fact]
    public void AnHourStartsAtTheHourAReadingFellIn()
        => Assert.Equal(
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            QualityWindows.StartOf(Sometime, QualityWindow.Hour));

    [Fact]
    public void AWindowKeepsTheKindOfClockItWasMeasuredOn()
        => Assert.Equal(DateTimeKind.Utc, QualityWindows.StartOf(Sometime, QualityWindow.Hour).Kind);

    [Fact]
    public void AWindowEndsWhereTheNextOneBegins()
        => Assert.Equal(
            new DateTime(2026, 9, 8, 13, 0, 0, DateTimeKind.Utc),
            QualityWindows.EndOf(QualityWindows.StartOf(Sometime, QualityWindow.Hour), QualityWindow.Hour));

    [Fact(DisplayName = "決定2: the two layers this domain keeps are a minute and an hour")]
    public void TheTwoLayersThisDomainKeepsAreAMinuteAndAnHour()
        => Assert.Equal([QualityWindow.Minute, QualityWindow.Hour], QualityWindows.All);

    [Fact]
    public void AWindowThisDomainDoesNotKeepHasNoLength()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityWindows.Length((QualityWindow)99));
}
