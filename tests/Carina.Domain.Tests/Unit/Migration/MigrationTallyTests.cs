using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationTallyTests
{
    [Fact]
    public void WhatAPopulationOfferedIsWhatItCarriedWhatItDidNotAndWhatNobodyCouldSay()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => MigrationTally.Rehydrate(Run, MigrationPopulation.Recordings, 5, 2, 2, 0));

        Assert.Contains("nobody could say", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 1)]
    [InlineData(0, 0, -1, 1)]
    [InlineData(0, 0, 1, -1)]
    public void ARunCountsNothingNegative(int offered, int carried, int notCarried, int unclassified)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationTally.Rehydrate(
            Run,
            MigrationPopulation.Recordings,
            offered,
            carried,
            notCarried,
            unclassified));
    }

    [Fact]
    public void TheProgrammeGuideIsNeverCountedAsRowsOfItsOwn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MigrationTally.Rehydrate(
            Run,
            MigrationPopulation.ProgrammeGuide,
            0,
            0,
            0,
            0));
    }
}
