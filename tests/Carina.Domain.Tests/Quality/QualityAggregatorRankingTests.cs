using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityAggregatorRankingTests
{
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

    [Fact(DisplayName = "BR-QD-001: only what was measured is put in an order")]
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

    [Fact(DisplayName = "BR-QD-001: a period nothing was counted in has nothing to put in an order")]
    public void APeriodNothingWasCountedInHasNothingToPutInAnOrder()
        => Assert.Empty(QualityAggregator.Rank(
            [.. Enumerable.Range(0, 3_514).Select(_ => QualityFactory.Unmeasured())],
            QualityAxis.Channel,
            ThresholdSense.Ceiling,
            take: 10));
}
