using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

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
    public void EveryRowWhoseTextWentThroughThatSubstitutionIsCounted()
    {
        SourceLedger ledger = Ledger(
            recordings: [Named(1, "an evening walk [字]"), Named(2, "an evening walk")],
            rules: [Rule(3, Terms(keyword: "hill"), SourceRuleReach.Plain)],
            reservations: [new SourceReservation(5, "a morning walk [新]", true)],
            channels: [Channel(11, SourceBroadcastKind.Terrestrial, InReach)]);

        Assert.Equal(2, MigrationTextLoss.RowsPastRestoring(ledger));
    }

    private static SourceRecording Named(long id, string name)
        => new(id, name, Began, Ended, InReach, Programme);
}
