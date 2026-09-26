using Carina.Domain.Migration;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationTextLossTests
{
    [Theory]
    [InlineData("[字]")]
    [InlineData("[新]")]
    [InlineData("[SS]")]
    public void TextTheSourceSystemPutSquareBracketsRoundCannotBeReadBackAsTheCharacterItWas(string said)
        => Assert.True(MigrationTextLoss.PastRestoring($"an evening walk {said}"));

    [Theory]
    [InlineData("an evening walk")]
    [InlineData("an evening walk \U0001F211")]
    [InlineData("an evening walk [1 of 3]")]
    public void TextThatCarriesNoSuchSubstitutionIsLeftAlone(string said)
        => Assert.False(MigrationTextLoss.PastRestoring(said));

    [Fact]
    public void TheEnclosedCharacterItselfIsWhatTheSubstitutionStandsFor()
        => Assert.Contains("[字]", MigrationTextLoss.Substitutions);

    [Fact]
    public void EveryCarriedTextThatWentThroughThatSubstitutionIsCounted()
        => Assert.Equal(
            2,
            MigrationTextLoss.RowsPastRestoring(["an evening walk [字]", "an evening walk", "hill [新]"]));
}
