using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRuleConversionTests
{
    [Fact]
    public void AKeywordAndTheWordsToLeaveOutBothCrossOver()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill upland", excluded: "repeat"), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.Equal("keyword=hill%20upland&exclude=repeat", carried.Query?.Value);
    }

    [Fact]
    public void AKeywordThatCarriesTheCharactersOfAQueryIsWrittenSoThatItReadsBackTheSame()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "a&b=c"), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal("keyword=a%26b%3Dc", carried.Query?.Value);
    }

    [Theory]
    [InlineData(SourceRuleFields.Title, "keyword=hill&fields=Title")]
    [InlineData(SourceRuleFields.Summary, "keyword=hill&fields=Description")]
    [InlineData(SourceRuleFields.Title | SourceRuleFields.Summary, "keyword=hill")]
    public void WhichFieldsTheSourceLookedAtCrossOver(SourceRuleFields fields, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", fields: fields), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(expected, carried.Query?.Value);
    }

    [Fact]
    public void ARuleThatSearchesTheExtendedBodyIsNotCarriedBecauseThisSystemHasNoSuchField()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(keyword: "hill", fields: SourceRuleFields.Title | SourceRuleFields.ExtendedBody),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.NoSuchFeature, refused.Refusal);
        Assert.Null(refused.Query);
    }

    [Fact]
    public void ARuleThatLeavesWordsOutOfDifferentFieldsThanItLooksInIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(
                    keyword: "hill",
                    excluded: "repeat",
                    fields: SourceRuleFields.Title | SourceRuleFields.Summary,
                    excludedFields: SourceRuleFields.Title),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
    }

    [Theory]
    [InlineData(SourceBroadcastKind.Terrestrial, "keyword=hill&type=IsdbT")]
    [InlineData(SourceBroadcastKind.BroadcastSatellite, "keyword=hill&type=IsdbSBs")]
    [InlineData(SourceBroadcastKind.CommunicationSatellite, "keyword=hill&type=IsdbSCs110")]
    public void WhichKindOfBroadcastTheSourceAskedForCrossesOver(SourceBroadcastKind kind, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", kinds: [kind]), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(expected, carried.Query?.Value);
    }

    [Fact]
    public void ARuleThatAsksForTwoKindsOfBroadcastAtOnceIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(
                    keyword: "hill",
                    kinds: [SourceBroadcastKind.Terrestrial, SourceBroadcastKind.BroadcastSatellite]),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
    }

    [Fact]
    public void ARuleThatAsksForTheOneKindOfBroadcastThisSystemCannotNameIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", kinds: [SourceBroadcastKind.Sky]), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
    }

    [Fact]
    public void TheChannelsARuleNamesCrossOverAsTheServicesTheyAre()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", services: [InReach]), SourceRuleReach.Plain),
            Rescanned(InReach));

        Assert.Equal("keyword=hill&channel=32736-1024", carried.Query?.Value);
    }

    [Fact]
    public void ARuleNamingAChannelTheRescanDoesNotAnswerForIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", services: [Elsewhere]), SourceRuleReach.Plain),
            Rescanned(InReach));

        Assert.Equal(MigrationRefusal.Unidentifiable, refused.Refusal);
        Assert.Null(refused.Query);
    }

    [Fact]
    public void AGenreCrossesOver()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", genres: [new SourceRuleGenre(9, null)]), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal("keyword=hill&genre=9", carried.Query?.Value);
    }

    [Fact]
    public void ARuleThatNarrowsToASubGenreIsNotCarriedBecauseThisSystemHasNoSuchCondition()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", genres: [new SourceRuleGenre(9, 2)]), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.NoSuchFeature, refused.Refusal);
    }

    [Fact]
    public void AWeekThatNamesEveryDayNarrowsNothingAndIsCarried()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: SourceWeek.EveryDay), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
    }

    [Theory]
    [InlineData(0b000_0001)]
    [InlineData(0b100_0000)]
    [InlineData(0)]
    public void ARuleThatRecordsOnSomeDaysOfTheWeekIsNotCarriedBecauseThisSystemHasNoSuchCondition(int days)
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: days), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.NoSuchFeature, refused.Refusal);
        Assert.Null(refused.Query);
    }

    [Fact]
    public void ARuleWhoseNameIsLongerThanARuleNameIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            new SourceRule(3, new string('x', 129), true, Terms(keyword: "hill"), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
    }

    [Fact]
    public void ARuleThatNarrowsNothingAtAllIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: string.Empty), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
    }

    [Fact]
    public void TheNameOfAConvertedRuleIsWhatTheSourceSystemCalledIt()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            new SourceRule(3, "an evening walk", true, Terms(keyword: "hill"), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal("an evening walk", carried.Name);
    }
}
