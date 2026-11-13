using Carina.Domain.Quality;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityAggregatorTests
{
    [Fact(DisplayName = "what nothing counted stays out of the denominator and is counted on its own")]
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

    [Fact(DisplayName = "a period in which nothing was counted reads as unmeasured rather than as none lost")]
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

    [Fact(DisplayName = "subjects whose tuner keeps no such statistic read as unsupported")]
    public void SubjectsWhoseTunerKeepsNoSuchStatisticReadAsUnsupported()
    {
        QualityTally tally = QualityAggregator.Tally([QualityFactory.Unsupported(), QualityFactory.Unsupported()]);

        Assert.Equal(QualityState.Unsupported, tally.State);
        Assert.Equal(2, tally.Unsupported);
        Assert.Equal(0, tally.Unmeasured);
    }

    [Fact(DisplayName = "a subject whose supply has stopped is unreachable even beside subjects that were measured")]
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

    [Fact(DisplayName = "a channel nothing counted is not a channel that lost nothing")]
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

    [Fact]
    public void UnderACeilingTheChannelThatLostTheMostComesFirst()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
                QualityFactory.Measured(0.0003, QualityFactory.Facet(service: 1_040)),
            ],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 3);

        Assert.Equal([1_032, 1_040, 1_024], ranked.Select(group => group.Key.Service!.Value));
    }

    [Fact]
    public void UnderAFloorTheTunerThatLockedLeastComesFirst()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.98, QualityFactory.Facet(tuner: "adapter0"), QualityFactory.LockRate()),
                QualityFactory.Measured(0.0, QualityFactory.Facet(tuner: "adapter2"), QualityFactory.LockRate()),
                QualityFactory.Measured(0.6, QualityFactory.Facet(tuner: "adapter1"), QualityFactory.LockRate()),
            ],
            QualityAxis.Tuner,
            ThresholdSense.Floor,
            take: 3);

        Assert.Equal(["adapter2", "adapter1", "adapter0"], ranked.Select(group => group.Key.Tuner!.Value));
    }

    [Fact(DisplayName = "only what was measured is put in an order")]
    public void OnlyWhatWasMeasuredIsPutInAnOrder()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Unmeasured(QualityFactory.Facet(service: 1_032)),
                QualityFactory.Unsupported(QualityFactory.Facet(service: 1_040)),
                QualityFactory.Unreachable(QualityFactory.Facet(service: 1_048)),
            ],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 10);

        QualityGrouping only = Assert.Single(ranked);

        Assert.Equal(1_024, only.Key.Service!.Value);
    }

    [Fact]
    public void AGroupThatWasPartlyMeasuredIsStillPutInAnOrderByWhatWasMeasured()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Unmeasured(QualityFactory.Facet(service: 1_032)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
            ],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 10);

        Assert.Equal([1_032, 1_024], ranked.Select(group => group.Key.Service!.Value));
        Assert.Equal(1, ranked[0].Tally.Unmeasured);
    }

    [Fact]
    public void TwoGroupsAsBadAsEachOtherAreOrderedByTheirOwnNamesRatherThanByChance()
    {
        QualityObservation[] observations =
        [
            QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_040)),
            QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_024)),
            QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
        ];

        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            observations,
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 3);

        IReadOnlyList<QualityGrouping> again = QualityAggregator.Rank(
            [.. observations.Reverse()],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 3);

        Assert.Equal([1_024, 1_032, 1_040], ranked.Select(group => group.Key.Service!.Value));
        Assert.Equal(ranked.Select(group => group.Key), again.Select(group => group.Key));
    }

    [Fact]
    public void OfTwoGroupsAsBadAsEachOtherTheOneThatWentBeyondTheLevelMoreOftenComesFirst()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
            ],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 2);

        Assert.Equal([1_032, 1_024], ranked.Select(group => group.Key.Service!.Value));
    }

    [Fact]
    public void AnOrderIsCutOffWhereItWasAskedToBe()
    {
        IReadOnlyList<QualityGrouping> ranked = QualityAggregator.Rank(
            [
                QualityFactory.Measured(0.00004, QualityFactory.Facet(service: 1_024)),
                QualityFactory.Measured(0.002, QualityFactory.Facet(service: 1_032)),
                QualityFactory.Measured(0.0003, QualityFactory.Facet(service: 1_040)),
            ],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 2);

        Assert.Equal([1_032, 1_040], ranked.Select(group => group.Key.Service!.Value));
    }

    [Fact]
    public void AnOrderOfNoneIsEmptyRatherThanEverything()
        => Assert.Empty(QualityAggregator.Rank(
            [QualityFactory.Measured(0.002)],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 0));

    [Fact]
    public void AnOrderCannotBeCutOffBeforeItBegins()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityAggregator.Rank(
            [QualityFactory.Measured(0.002)],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: -1));

    [Fact]
    public void AnOrderIsReadInADirectionThisDomainNames()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityAggregator.Rank(
            [QualityFactory.Measured(0.002)],
            QualityAxis.Channel,
            (ThresholdSense)0,
            take: 1));

    [Fact(DisplayName = "a period nothing was counted in has nothing to put in an order")]
    public void APeriodNothingWasCountedInHasNothingToPutInAnOrder()
        => Assert.Empty(QualityAggregator.Rank(
            [.. Enumerable.Range(0, 3_514).Select(_ => QualityFactory.Unmeasured())],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 10));
}
