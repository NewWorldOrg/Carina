using System.Numerics;

using Carina.Domain.Migration;
using Carina.Domain.Programmes;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationRuleConversionTests
{
    private static readonly SourceRuleReach Inexpressible =
        SourceRuleReach.Plain with { UsesRegularExpression = true };

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
    public void ARuleThatNarrowsToASubGenreCarriesThatSubGenreAndNotTheWholeGenre()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", genres: [new SourceRuleGenre(9, 2)]), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.Equal("keyword=hill&subgenre=9-2", carried.Query?.Value);
    }

    [Fact]
    public void ARuleNamingOneGenreWholeAndOneSubGenreCarriesBothAsTheyWereAsked()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(
                3,
                Terms(keyword: "hill", genres: [new SourceRuleGenre(6, null), new SourceRuleGenre(9, 2)]),
                SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal("keyword=hill&genre=6&subgenre=9-2", carried.Query?.Value);
    }

    [Fact]
    public void ARuleNamingASubGenreThisSystemDoesNotCountThatHighIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", genres: [new SourceRuleGenre(9, 16)]), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
        Assert.Null(refused.Query);
    }

    [Fact]
    public void AWeekThatNamesEveryDayNarrowsNothingAndSaysNoDayAtAll()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: SourceWeek.EveryDay), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.Equal("keyword=hill", carried.Query?.Value);
    }

    [Theory]
    [InlineData(0b000_0001, "keyword=hill&day=Sunday")]
    [InlineData(0b000_0010, "keyword=hill&day=Monday")]
    [InlineData(0b000_0100, "keyword=hill&day=Tuesday")]
    [InlineData(0b000_1000, "keyword=hill&day=Wednesday")]
    [InlineData(0b001_0000, "keyword=hill&day=Thursday")]
    [InlineData(0b010_0000, "keyword=hill&day=Friday")]
    [InlineData(0b100_0000, "keyword=hill&day=Saturday")]
    [InlineData(0b100_0001, "keyword=hill&day=Sunday&day=Saturday")]
    public void TheDaysOfTheWeekARuleRecordsOnCrossOver(int days, string expected)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: days), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.Equal(expected, carried.Query?.Value);
    }

    [Fact]
    public void AWeekThatNamesNoDayAtAllIsNotCarried()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: 0), SourceRuleReach.Plain),
            Rescanned());

        Assert.Equal(MigrationRefusal.Inexpressible, refused.Refusal);
        Assert.Null(refused.Query);
    }

    [Fact]
    public void NoWeekIsEverCarriedAsMoreDaysThanTheSourceRuleNamed()
    {
        for (int days = 1; days <= SourceWeek.EveryDay; days++)
        {
            MigrationRuleConversion carried = MigrationRuleConversion.Of(
                Rule(3, Terms(keyword: "hill", days: days), SourceRuleReach.Plain),
                Rescanned());

            IReadOnlyList<DayOfWeek> said = DaysSaid(carried);
            int named = BitOperations.PopCount((uint)days);

            Assert.Equal(named is ProgrammeSearch.DaysInTheWeek ? 0 : named, said.Count);

            foreach (DayOfWeek day in said)
            {
                Assert.NotEqual(0, days & (1 << (int)day));
            }
        }
    }

    [Fact]
    public void AWeekThatNarrowsIsNeverCarriedAsAWeekThatNarrowsNothing()
    {
        for (int days = 1; days < SourceWeek.EveryDay; days++)
        {
            MigrationRuleConversion carried = MigrationRuleConversion.Of(
                Rule(3, Terms(keyword: "hill", days: days), SourceRuleReach.Plain),
                Rescanned());

            Assert.NotEmpty(DaysSaid(carried));
        }
    }

    [Theory]
    [InlineData(0b000_0001)]
    [InlineData(0b100_0001)]
    [InlineData(0b011_1111)]
    public void ARuleCarriedWithDaysOfItsOwnKeepsThemAgainstADayThatBeginsElsewhere(int days)
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: days), SourceRuleReach.Plain),
            Rescanned());

        Assert.True(carried.Expressible);
        Assert.True(carried.NarrowedByDay);
    }

    [Fact]
    public void ARuleThatNamesEveryDayIsNarrowedByNoneOfThem()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: SourceWeek.EveryDay), SourceRuleReach.Plain),
            Rescanned());

        Assert.False(carried.NarrowedByDay);
    }

    [Fact]
    public void ARuleNobodyCouldCarryIsNarrowedByNoDayBecauseItNeverCrossedOver()
    {
        MigrationRuleConversion refused = MigrationRuleConversion.Of(
            Rule(3, Terms(keyword: "hill", days: 0b000_0001), Inexpressible),
            Rescanned());

        Assert.False(refused.Expressible);
        Assert.False(refused.NarrowedByDay);
    }

    [Fact]
    public void TheRulesCarriedWithDaysOfTheirOwnAreCountedOverTheWholeLedger()
    {
        SourceLedger ledger = Ledger(rules:
        [
            Rule(3, Terms(keyword: "hill", days: 0b000_0001), SourceRuleReach.Plain),
            Rule(4, Terms(keyword: "hill", days: 0b100_0001), SourceRuleReach.Plain),
            Rule(5, Terms(keyword: "hill", days: SourceWeek.EveryDay), SourceRuleReach.Plain),
            Rule(6, Terms(keyword: "hill", days: 0b000_0010), Inexpressible),
        ]);

        Assert.Equal(2, MigrationRuleConversion.RulesNarrowedByDay(ledger, Rescanned()));
    }

    private static IReadOnlyList<DayOfWeek> DaysSaid(MigrationRuleConversion carried)
        => [
            .. (carried.Query?.Value ?? string.Empty)
                .Split('&')
                .Where(pair => pair.StartsWith("day=", StringComparison.Ordinal))
                .Select(pair => Enum.Parse<DayOfWeek>(pair["day=".Length..])),
        ];

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
