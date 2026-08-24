namespace Carina.Contracts.Tests;

public sealed class RecordingExtensionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static ExtendSessionRequest Asking(TimeSpan from) =>
        new() { EndsAt = Now + from };

    [Fact]
    public void AnEndLaterThanBothTheCurrentOneAndNowIsAnExtension()
    {
        Assert.Empty(Asking(TimeSpan.FromMinutes(40)).Validate(Now.AddMinutes(30), Now));
    }

    [Fact]
    public void AnEndAtTheVeryTimeTheRecordingAlreadyStopsIsNotAnExtension()
    {
        Assert.Contains(
            Asking(TimeSpan.FromMinutes(30)).Validate(Now.AddMinutes(30), Now),
            problem => problem.StartsWith("endsAt:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnEndEarlierThanTheCurrentOneIsNotAnExtension()
    {
        Assert.Contains(
            Asking(TimeSpan.FromMinutes(20)).Validate(Now.AddMinutes(30), Now),
            problem => problem.StartsWith("endsAt:", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void AnEndThatHasAlreadyPassedIsRefusedEvenThoughItFollowsTheCurrentOne()
    {
        IReadOnlyList<string> problems = Asking(TimeSpan.FromMinutes(-5))
            .Validate(Now.AddMinutes(-10), Now);

        Assert.Contains(
            problems,
            problem => problem.Contains(
                $"expected a time after {Now:O}",
                StringComparison.Ordinal
            )
        );
    }

    [Fact]
    public void AnEndAtThisVeryMomentIsRefusedEvenThoughItFollowsTheCurrentOne()
    {
        Assert.NotEmpty(Asking(TimeSpan.Zero).Validate(Now.AddMinutes(-10), Now));
    }

    [Fact]
    public void TheOlderReadingOfTheSameRequestStillOnlyLooksAtTheEndItWouldReplace()
    {
        Assert.Empty(Asking(TimeSpan.FromMinutes(-5)).Validate(Now.AddMinutes(-10)));
    }

    [Fact]
    public void EveryRefusalNamesTheFieldItRefused()
    {
        Assert.All(
            Asking(TimeSpan.FromMinutes(-5)).Validate(Now.AddMinutes(30), Now),
            problem => Assert.StartsWith("endsAt:", problem, StringComparison.Ordinal)
        );
    }
}
