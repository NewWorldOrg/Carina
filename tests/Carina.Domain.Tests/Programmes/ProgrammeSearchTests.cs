using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeSearchTests
{
    private static readonly DateTime From = new(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AKeywordOfOneLetterWouldWalkTheWholeTableAndIsRefused()
        => Assert.Null(ProgrammeSearch.For("あ", null, null));

    [Fact]
    public void TwoLettersIsEnoughToAskWith()
        => Assert.NotNull(ProgrammeSearch.For("news", null, null));

    [Fact]
    public void SurroundingSpaceDoesNotCountTowardsTheKeyword()
        => Assert.Null(ProgrammeSearch.For("  a  ", null, null));

    [Fact]
    public void AKeywordIsKeptWithoutItsSurroundingSpace()
        => Assert.Equal("news", ProgrammeSearch.For("  news  ", null, null)!.Keyword);

    [Fact]
    public void APageSizeNobodyNamedFallsBackRatherThanFetchingEverything()
        => Assert.Equal(ProgrammeSearch.DefaultPerPage, ProgrammeSearch.For("news", null, null)!.PerPage);

    [Fact]
    public void APageSizeBeyondTheCeilingIsBroughtDownToIt()
        => Assert.Equal(
            ProgrammeSearch.MostPerPage,
            ProgrammeSearch.For("news", null, null, perPage: 5_000)!.PerPage);

    [Fact]
    public void APageSizeOfNothingIsNotHonoured()
        => Assert.Equal(ProgrammeSearch.DefaultPerPage, ProgrammeSearch.For("news", null, null, perPage: 0)!.PerPage);

    [Fact]
    public void PagesCountFromOneHoweverTheyAreAskedFor()
        => Assert.Equal(1, ProgrammeSearch.For("news", null, null, page: -3)!.Page);

    [Fact]
    public void ASpanLongerThanAMonthIsRefused()
        => Assert.Null(ProgrammeSearch.For("news", From, From.AddDays(32)));

    [Fact]
    public void AMonthIsAllowed()
        => Assert.NotNull(ProgrammeSearch.For("news", From, From + ProgrammeSearch.LongestSpan));

    [Fact]
    public void ASpanThatRunsBackwardsIsRefused()
        => Assert.Null(ProgrammeSearch.For("news", From, From.AddHours(-1)));

    [Fact]
    public void AMomentWithoutAnOffsetIsRefusedRatherThanGuessedAt()
        => Assert.Null(ProgrammeSearch.For(
            "news",
            new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Unspecified),
            null));

    [Fact]
    public void OneSidedSpansAreAllowedBecauseTheKeywordAlreadyNarrows()
    {
        Assert.NotNull(ProgrammeSearch.For("news", From, null));
        Assert.NotNull(ProgrammeSearch.For("news", null, From));
    }
}
