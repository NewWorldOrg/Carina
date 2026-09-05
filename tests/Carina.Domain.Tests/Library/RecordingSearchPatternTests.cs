using Carina.Domain.Library;

namespace Carina.Domain.Tests.Library;

public sealed class RecordingSearchPatternTests
{
    [Fact]
    public void AWordWithNothingSpecialInItIsLookedForAnywhereInTheText()
        => Assert.Equal("%ニュース%", RecordingSearchPattern.Containing("ニュース"));

    [Theory]
    [InlineData("100%", @"%100\%%")]
    [InlineData("a_b", @"%a\_b%")]
    [InlineData(@"back\slash", @"%back\\slash%")]
    [InlineData("%_%", @"%\%\_\%%")]
    public void TheLettersThatWouldOtherwiseStandForAnythingAreAskedForLiterally(string word, string pattern)
        => Assert.Equal(pattern, RecordingSearchPattern.Containing(word));

    [Fact]
    public void TheLetterThatUndoesTheOthersIsUndoneFirstSoItNeverEscapesItsOwnEscape()
        => Assert.Equal(@"%\\\%%", RecordingSearchPattern.Containing(@"\%"));

    [Fact]
    public void AnEmptyWordMatchesEverythingRatherThanThrowing()
        => Assert.Equal("%%", RecordingSearchPattern.Containing(string.Empty));
}
