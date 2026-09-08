using Carina.Contracts;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Programmes;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class MigrationRuleQueryReadsBackTests
{
    [Fact]
    public void WhatARuleWasConvertedIntoIsReadBackAsTheSearchItStandsFor()
    {
        SourceRule rule = new(
            3,
            "hill",
            true,
            new SourceRuleTerms(
                "hill upland",
                "repeat",
                SourceRuleFields.Title,
                SourceRuleFields.Title,
                [SourceBroadcastKind.Terrestrial],
                [InReach],
                [new SourceRuleGenre(9, null)],
                SourceWeek.EveryDay),
            SourceRuleReach.Plain);

        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            rule,
            RescannedService.InReach(Rescanned()));

        ProgrammeSearch read = Assert.IsType<ProgrammeSearch>(
            ProgrammeSearchQuery.Read(carried.Query?.Value));

        Assert.Equal(["hill", "upland"], read.Words);
        Assert.Equal(["repeat"], read.ExcludedWords);
        Assert.Equal([ProgrammeField.Title], read.Fields);
        Assert.Equal([9], read.Genres);
        Assert.Equal(TuneSystem.IsdbT, read.System);
        Assert.Equal([new ProgrammeService(32736, 1024)], read.Channels);
    }

    [Fact]
    public void AKeywordCarryingTheCharactersOfAQueryReadsBackWhole()
    {
        MigrationRuleConversion carried = MigrationRuleConversion.Of(
            Rule(3, keyword: "a&b=c"),
            RescannedService.InReach(Rescanned()));

        ProgrammeSearch read = Assert.IsType<ProgrammeSearch>(
            ProgrammeSearchQuery.Read(carried.Query?.Value));

        Assert.Equal(["a&b=c"], read.Words);
    }
}
