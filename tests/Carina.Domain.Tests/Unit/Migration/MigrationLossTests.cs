using Carina.Domain.Migration;

using static Carina.Domain.Tests.Unit.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Unit.Migration;

[Trait("Category", "Unit")]
public sealed class MigrationLossTests
{
    [Fact]
    public void WhatTheNewSystemIsShortOfIsOneOfTheThingsTheRecordKnowsToName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationLoss.Rehydrate(Run, (MigrationLossSubject)9, 1));
    }

    [Theory]
    [InlineData(MigrationLossSubject.DuplicateAvoidance)]
    [InlineData(MigrationLossSubject.EnclosedCharacters)]
    [InlineData(MigrationLossSubject.DayBoundary)]
    [InlineData(MigrationLossSubject.MarkWords)]
    public void ALossIsSomethingCarriedThatArrivedDiminished(MigrationLossSubject subject)
    {
        Assert.Equal(subject, MigrationLoss.Rehydrate(Run, subject, 17).Subject);
    }

    [Fact]
    public void ALossCountsNothingNegative()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => MigrationLoss.Rehydrate(Run, MigrationLossSubject.EnclosedCharacters, -1));

    [Fact]
    public void ALossOfNothingAtAllIsStillCountedRatherThanLeftUnsaid()
        => Assert.Equal(0, MigrationLoss.Rehydrate(Run, MigrationLossSubject.DuplicateAvoidance, 0).Affected);

    [Fact]
    public void TheRecordIsOnlyEverShortOfWhatTheRunKnowsHowToCount()
    {
        Assert.Equal(MigrationLossSubjects.All.Count, MigrationLoss.EveryOne(Run, new([], [], 0, 0, 0, 0)).Count);
    }

    [Fact]
    public void EveryLossSaysHowManyOfWhatWasCarriedItReaches()
    {
        MigrationAftermath aftermath = new([], [], 17, 48, 5, 2);

        IReadOnlyList<MigrationLoss> told = MigrationLoss.EveryOne(Run, aftermath);

        Assert.Equal(
            17,
            told.Single(loss => loss.Subject is MigrationLossSubject.DuplicateAvoidance).Affected);
        Assert.Equal(
            48,
            told.Single(loss => loss.Subject is MigrationLossSubject.EnclosedCharacters).Affected);
        Assert.Equal(
            5,
            told.Single(loss => loss.Subject is MigrationLossSubject.DayBoundary).Affected);
        Assert.Equal(
            2,
            told.Single(loss => loss.Subject is MigrationLossSubject.MarkWords).Affected);
    }
}
