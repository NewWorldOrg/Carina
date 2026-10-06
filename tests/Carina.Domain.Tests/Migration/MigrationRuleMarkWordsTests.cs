using Carina.Domain.Migration;
using Carina.Domain.Programmes;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRuleMarkWordsTests
{
    [Fact]
    public void ARuleLookingForTheNewMarkAndLeavingOutTheRerunLetterCarriesBothAsMarks()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(
                    keyword: "[新]",
                    excluded: "再",
                    kinds: [SourceBroadcastKind.Terrestrial],
                    genres: [new SourceRuleGenre(7, 0)]),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.True(carried.MarkWordsReplaced);
        Assert.Equal("subgenre=7-0&mark=New&excludeMark=Rerun&type=IsdbT", carried.Query?.Value);
    }

    [Theory]
    [InlineData("\U0001F21F", "mark=New")]
    [InlineData("[再]", "mark=Rerun")]
    [InlineData("［終］", "mark=Final")]
    [InlineData("【初】", "mark=Premiere")]
    [InlineData("[ＨＶ]", "mark=HighDefinition")]
    [InlineData("[SS]", "mark=SurroundStereo")]
    [InlineData("[ppv]", "mark=PayPerView")]
    [InlineData("[鍵]", "mark=ParentalLock")]
    [InlineData("⚿", "mark=ParentalLock")]
    [InlineData("字", "mark=Captioned")]
    [InlineData("[字] [新]", "mark=Captioned&mark=New")]
    [InlineData("[新][字]", "mark=Captioned&mark=New")]
    public void AWordThatIsOnlyAMarkBecomesTheMark(string keyword, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: keyword), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.MarkWordsReplaced);
        Assert.Equal(expected, carried.Query?.Value);
    }

    [Theory]
    [InlineData("[新]アニメ", "keyword=%E3%82%A2%E3%83%8B%E3%83%A1&mark=New")]
    [InlineData("アニメ\U0001F21F", "keyword=%E3%82%A2%E3%83%8B%E3%83%A1&mark=New")]
    [InlineData("アニメ[新]特集", "keyword=%E3%82%A2%E3%83%8B%E3%83%A1%20%E7%89%B9%E9%9B%86&mark=New")]
    [InlineData("[新] hill", "keyword=hill&mark=New")]
    public void AMarkWrittenAmongWordsIsTakenOutAndTheWordsAroundItStay(string keyword, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: keyword), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.MarkWordsReplaced);
        Assert.Equal(expected, carried.Query?.Value);
    }

    [Theory]
    [InlineData("新宿 hill", "keyword=%E6%96%B0%E5%AE%BF%20hill")]
    [InlineData("hill 再会", "keyword=hill%20%E5%86%8D%E4%BC%9A")]
    [InlineData("S hill", "keyword=S%20hill")]
    [InlineData("[hill]", "keyword=%5Bhill%5D")]
    [InlineData("[新 hill", "keyword=%5B%E6%96%B0%20hill")]
    public void LettersThatOnlyLookLikeAMarkStayWords(string keyword, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: keyword), SourceRuleReach.Plain),
            Rescanned());

        Assert.False(carried.MarkWordsReplaced);
        Assert.Equal(expected, carried.Query?.Value);
    }

    [Fact]
    public void AMarkLeftOutOfOtherFieldsThanTheWordsAreLookedForInStillCrossesOverBecauseAMarkHasNoField()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(
                    keyword: "hill",
                    excluded: "[再]",
                    fields: SourceRuleFields.Title | SourceRuleFields.Summary,
                    excludedFields: SourceRuleFields.Title),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.Equal("keyword=hill&excludeMark=Rerun", carried.Query?.Value);
    }

    [Fact]
    public void ARuleLeftWithOnlyMarksSaysNothingAboutWhereToLookForWords()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "[新]", fields: SourceRuleFields.Title), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal("mark=New", carried.Query?.Value);
    }

    [Theory]
    [InlineData("夏", "")]
    [InlineData("hill", "再 夏")]
    [InlineData("S", "")]
    public void ARuleWhoseWordsWouldNotReadAsASearchIsNotCarriedRatherThanCarriedAndTurnedOff(
        string keyword,
        string excluded)
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: keyword, excluded: excluded), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
        Assert.Null(refused.Query);
        Assert.False(refused.MarkWordsReplaced);
    }

    [Fact]
    public void ARuleWithNoMarkWordReplacesNothing()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", excluded: "repeat"), SourceRuleReach.Plain),
            Rescanned());

        Assert.False(carried.MarkWordsReplaced);
    }

    [Fact]
    public void TheRulesCarriedWithTheirMarkWordsReplacedAreCountedOverTheWholeLedger()
    {
        SourceLedger ledger = Ledger(rules:
        [
            Rule(3, Terms(keyword: "[新]", excluded: "再"), SourceRuleReach.Plain),
            Rule(4, Terms(keyword: "\U0001F21Fhill"), SourceRuleReach.Plain),
            Rule(5, Terms(keyword: "hill"), SourceRuleReach.Plain),
            Rule(6, Terms(keyword: "[新]"), SourceRuleReach.Plain with { UsesRegularExpression = true }),
            Rule(7, Terms(keyword: "[新] 夏"), SourceRuleReach.Plain with { CaseSensitive = true }),
        ]);

        Assert.Equal(2, MigrationRuleConversion.RulesWithMarkWordsReplaced(ledger, Rescanned()));
    }

    [Fact]
    public void EveryMarkCanBeAskedForByItsLettersInBrackets()
    {
        foreach (ProgrammeMarkSymbol symbol in ProgrammeMarks.Symbols.Where(symbol => symbol.Mark is not ProgrammeMark.ParentalLock))
        {
            string letters = ProgrammeSearchText.Folded(symbol.Symbol);
            MigrationRuleConversion carried = MigrationRuleConversion.Of(
                Rule(3, Terms(keyword: $"[{letters}]"), SourceRuleReach.Plain),
                Rescanned());

            Assert.Equal($"mark={symbol.Mark}", carried.Query?.Value);
        }
    }
}
