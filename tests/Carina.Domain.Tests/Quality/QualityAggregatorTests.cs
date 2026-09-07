using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityAggregatorTests
{
    [Fact(DisplayName = "BR-QD-001: what nothing counted stays out of the denominator and is counted on its own")]
    public void WhatNothingCountedStaysOutOfTheDenominatorAndIsCountedOnItsOwn()
    {
        QualityTally tally = QualityAggregator.Tally(
        [
            QualityFactory.Measured(0.00004),
            QualityFactory.Unmeasured(),
            QualityFactory.Unmeasured(),
        ]);

        Assert.Equal(3, tally.Subjects);
        Assert.Equal(1, tally.Measured);
        Assert.Equal(2, tally.Unmeasured);
        Assert.Equal(0.00004, tally.Average!.Value, 12);
    }

    [Fact(DisplayName = "BR-QD-001: a period in which nothing was counted reads as unmeasured rather than as none lost")]
    public void APeriodInWhichNothingWasCountedReadsAsUnmeasuredRatherThanAsNoneLost()
    {
        QualityTally tally = QualityAggregator.Tally([.. Enumerable.Range(0, 3_514).Select(_ => QualityFactory.Unmeasured())]);

        Assert.Equal(QualityState.Unmeasured, tally.State);
        Assert.Equal(3_514, tally.Unmeasured);
        Assert.Equal(0, tally.Measured);
        Assert.Null(tally.Average);
        Assert.Null(tally.Lowest);
        Assert.Null(tally.Highest);
    }

    [Fact]
    public void APeriodHoldingNothingAtAllHasNothingToMeasureRatherThanNothingWrong()
    {
        QualityTally tally = QualityAggregator.Tally([]);

        Assert.Equal(QualityState.NothingToMeasure, tally.State);
        Assert.Equal(0, tally.Subjects);
        Assert.Null(tally.Average);
    }

    [Fact(DisplayName = "BR-QD-009: subjects whose tuner keeps no such statistic read as unsupported")]
    public void SubjectsWhoseTunerKeepsNoSuchStatisticReadAsUnsupported()
    {
        QualityTally tally = QualityAggregator.Tally([QualityFactory.Unsupported(), QualityFactory.Unsupported()]);

        Assert.Equal(QualityState.Unsupported, tally.State);
        Assert.Equal(2, tally.Unsupported);
        Assert.Equal(0, tally.Unmeasured);
    }

    [Fact(DisplayName = "BR-QD-007: a subject whose supply has stopped is unreachable even beside subjects that were measured")]
    public void ASubjectWhoseSupplyHasStoppedIsUnreachableEvenBesideSubjectsThatWereMeasured()
    {
        QualityTally tally = QualityAggregator.Tally([QualityFactory.Measured(0.00004), QualityFactory.Unreachable()]);

        Assert.Equal(QualityState.Unreachable, tally.State);
        Assert.Equal(1, tally.Unreachable);
        Assert.Equal(0, tally.Unmeasured);
        Assert.Equal(1, tally.Measured);
    }

    [Fact]
    public void SomethingUnsupportedIsNotCountedAmongTheThingsNobodyCounted()
    {
        QualityTally tally = QualityAggregator.Tally(
        [
            QualityFactory.Measured(0.00004),
            QualityFactory.Unmeasured(),
            QualityFactory.Unsupported(),
        ]);

        Assert.Equal(1, tally.Unmeasured);
        Assert.Equal(1, tally.Unsupported);
        Assert.Equal(QualityState.Good, tally.State);
    }

    [Fact]
    public void OneReadingBeyondTheWarningLevelIsEnoughForThePeriodToHaveReachedIt()
    {
        QualityTally tally = QualityAggregator.Tally(
        [
            QualityFactory.Measured(0.00004),
            QualityFactory.Measured(0.0003),
        ]);

        Assert.Equal(QualityState.AtOrAboveWarning, tally.State);
        Assert.Equal(1, tally.Good);
        Assert.Equal(1, tally.Warning);
        Assert.Equal(0, tally.MayNotBeWatchable);
        Assert.Equal(1, tally.BeyondThreshold);
    }

    [Fact]
    public void TheThreeGradesAreCountedApartFromEachOther()
    {
        QualityTally tally = QualityAggregator.Tally(
        [
            QualityFactory.Measured(0.00004),
            QualityFactory.Measured(0.0003),
            QualityFactory.Measured(0.002),
        ]);

        Assert.Equal(1, tally.Good);
        Assert.Equal(1, tally.Warning);
        Assert.Equal(1, tally.MayNotBeWatchable);
        Assert.Equal(2, tally.BeyondThreshold);
    }

    [Fact]
    public void TheLowestAndHighestOfAPeriodComeOnlyFromWhatWasMeasured()
    {
        QualityTally tally = QualityAggregator.Tally(
        [
            QualityFactory.Measured(0.002),
            QualityFactory.Unmeasured(),
            QualityFactory.Measured(0.00004),
            QualityFactory.Unreachable(),
        ]);

        Assert.Equal(0.00004, tally.Lowest!.Value, 12);
        Assert.Equal(0.002, tally.Highest!.Value, 12);
        Assert.Equal(0.00102, tally.Average!.Value, 12);
    }

    [Fact]
    public void TheWorstOfAPeriodIsTheHighestUnderACeilingAndTheLowestUnderAFloor()
    {
        QualityTally tally = QualityAggregator.Tally([QualityFactory.Measured(0.002), QualityFactory.Measured(0.00004)]);

        Assert.Equal(0.002, tally.Worst(ThresholdSense.Ceiling)!.Value, 12);
        Assert.Equal(0.00004, tally.Worst(ThresholdSense.Floor)!.Value, 12);
    }

    [Fact]
    public void ATallyIsTakenOfObservationsThatWereHandedOver()
        => Assert.Throws<ArgumentNullException>(() => QualityAggregator.Tally(null!));

    [Fact]
    public void EveryObservationInATallyIsAnObservation()
        => Assert.Throws<ArgumentNullException>(() => QualityAggregator.Tally([null!]));

    [Fact]
    public void ObservationsAreGatheredUnderTheChannelTheyWereTakenOn()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
                QualityFactory.Unmeasured(QualityFactory.Facet(service: 1_032)),
            ],
            QualityAxis.Channel);

        Assert.Equal(2, groups.Count);
        Assert.Equal(1_024, groups[0].Key.Service!.Value);
        Assert.Equal(1, groups[0].Tally.Subjects);
        Assert.Equal(1_032, groups[1].Key.Service!.Value);
        Assert.Equal(2, groups[1].Tally.Subjects);
        Assert.Equal(1, groups[1].Tally.Unmeasured);
    }

    [Fact]
    public void AGroupingByChannelSaysNothingAboutWhichTunerOrHourItCameFrom()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [QualityFactory.Measured(0.00004)],
            QualityAxis.Channel);

        Assert.Null(groups[0].Key.Tuner);
        Assert.Null(groups[0].Key.HourOfDay);
        Assert.Null(groups[0].Key.Kind);
    }

    [Fact]
    public void ObservationsAreGatheredUnderTheTunerThatTookThem()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(tuner: "adapter1")),
                QualityFactory.Measured(0.002, QualityFactory.Facet(tuner: "adapter0")),
            ],
            QualityAxis.Tuner);

        Assert.Equal(["adapter0", "adapter1"], groups.Select(group => group.Key.Tuner!.Value));
    }

    [Fact]
    public void ObservationsAreGatheredUnderTheHourOfDayTheyBelongTo()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(hourOfDay: 21)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(hourOfDay: 3)),
                QualityFactory.Measured(0.0003, QualityFactory.Facet(hourOfDay: 21)),
            ],
            QualityAxis.TimeOfDay);

        Assert.Equal([3, 21], groups.Select(group => group.Key.HourOfDay!.Value));
        Assert.Equal(2, groups[1].Tally.Subjects);
    }

    [Fact]
    public void ObservationsAreGatheredUnderTheKindOfBroadcastTheyCameFrom()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Unreachable(QualityFactory.Facet(kind: TuneSystem.IsdbSBs)),
                QualityFactory.Measured(0.00004, QualityFactory.Facet(kind: TuneSystem.IsdbT)),
                QualityFactory.Unreachable(QualityFactory.Facet(kind: TuneSystem.IsdbSCs110)),
            ],
            QualityAxis.Kind);

        Assert.Equal(
            [TuneSystem.IsdbT, TuneSystem.IsdbSBs, TuneSystem.IsdbSCs110],
            groups.Select(group => group.Key.Kind!.Value));
        Assert.Equal(QualityState.Good, groups[0].Tally.State);
        Assert.Equal(QualityState.Unreachable, groups[1].Tally.State);
        Assert.Equal(QualityState.Unreachable, groups[2].Tally.State);
    }

    [Fact]
    public void SeveralAxesAtOnceGatherObservationsUnderEachCombinationSeenAndNoOther()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024, tuner: "adapter0")),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_024, tuner: "adapter1")),
                QualityFactory.Measured(0.0003, QualityFactory.Facet(service: 1_024, tuner: "adapter0")),
            ],
            QualityAxis.Channel | QualityAxis.Tuner);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Tally.Subjects);
        Assert.Equal(1, groups[1].Tally.Subjects);
    }

    [Fact]
    public void GatheringAlongNoAxisAtAllLeavesOneGroupHoldingThePeriodEntire()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
            ],
            QualityAxis.Whole);

        QualityGrouping only = Assert.Single(groups);

        Assert.Equal(2, only.Tally.Subjects);
        Assert.Null(only.Key.Service);
    }

    [Fact]
    public void APeriodHoldingNothingIsGatheredIntoNoGroupsAtAll()
        => Assert.Empty(QualityAggregator.GroupBy([], QualityAxis.Channel));

    [Fact]
    public void ObservationsAreGatheredAlongAxesThisDomainNames()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => QualityAggregator.GroupBy([QualityFactory.Measured(0.00004)], (QualityAxis)64));

    [Fact(DisplayName = "BR-QD-001: a channel nothing counted is not a channel that lost nothing")]
    public void AChannelNothingCountedIsNotAChannelThatLostNothing()
    {
        IReadOnlyList<QualityGrouping> groups = QualityAggregator.GroupBy(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Unmeasured(QualityFactory.Facet(service: 1_032)),
            ],
            QualityAxis.Channel);

        Assert.Equal(QualityState.Good, groups[0].Tally.State);
        Assert.Equal(QualityState.Unmeasured, groups[1].Tally.State);
        Assert.Null(groups[1].Tally.Average);
    }
}
