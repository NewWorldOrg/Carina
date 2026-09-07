using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityBoardTests
{
    [Fact(DisplayName = "an unmeasured recording is left out of the share and counted beside it")]
    public void AnUnmeasuredRecordingIsLeftOutOfTheShareAndCountedBesideIt()
    {
        IReadOnlyList<QualityMeasure> whole = QualityBoard.Whole(
            [
                QualityFactory.Row(dropped: 0, total: 1_000_000),
                QualityFactory.Row(dropped: null, total: null, scrambled: null),
            ],
            [QualityMetric.PacketsLost],
            QualityFactory.Bands());

        QualityTally tally = whole[0].Tally;

        Assert.Equal(2, tally.Subjects);
        Assert.Equal(1, tally.Measured);
        Assert.Equal(1, tally.Unmeasured);
        Assert.Equal(0, tally.Average);
        Assert.Equal(QualityState.Good, tally.State);
    }

    [Fact(DisplayName = "a period where nothing was measured reads as unmeasured, not as good")]
    public void APeriodWhereNothingWasMeasuredReadsAsUnmeasured()
    {
        QualityTally tally = QualityBoard.Whole(
            [
                QualityFactory.Row(dropped: null, total: null, scrambled: null),
                QualityFactory.Row(dropped: null, total: null, scrambled: null),
            ],
            [QualityMetric.PacketsLost],
            QualityFactory.Bands())[0].Tally;

        Assert.Equal(QualityState.Unmeasured, tally.State);
        Assert.Equal(2, tally.Unmeasured);
        Assert.Null(tally.Average);
    }

    [Fact(DisplayName = "a period holding no recording at all reads as nothing to measure")]
    public void APeriodHoldingNoRecordingAtAllReadsAsNothingToMeasure()
    {
        QualityTally tally = QualityBoard.Whole([], QualityMetrics.All, QualityFactory.Bands())[0].Tally;

        Assert.Equal(QualityState.NothingToMeasure, tally.State);
        Assert.Equal(0, tally.Subjects);
    }

    [Fact(DisplayName = "a group beyond the threshold still says how many were measured")]
    public void AGroupBeyondTheThresholdStillSaysHowManyWereMeasured()
    {
        QualityTally tally = QualityBoard.Whole(
            [
                QualityFactory.Row(dropped: 0, total: 1_000_000),
                QualityFactory.Row(dropped: 2_000, total: 1_000_000),
                QualityFactory.Row(dropped: null, total: null, scrambled: null),
            ],
            [QualityMetric.PacketsLost],
            QualityFactory.Bands())[0].Tally;

        Assert.Equal(QualityState.AtOrAboveWarning, tally.State);
        Assert.Equal(3, tally.Subjects);
        Assert.Equal(2, tally.Measured);
        Assert.Equal(1, tally.BeyondThreshold);
        Assert.Equal(1, tally.Unmeasured);
        Assert.Equal(1, tally.MayNotBeWatchable);
        Assert.Equal(0, tally.Warning);
    }

    [Fact]
    public void EveryGroupCarriesEveryMeasureThatWasAskedFor()
    {
        IReadOnlyList<QualityGroupReading> grouped = QualityBoard.Grouped(
            [
                QualityFactory.Row(service: 1_024),
                QualityFactory.Row(service: 1_032, dropped: 500, total: 1_000_000),
            ],
            QualityAxis.Channel,
            QualityMetrics.All,
            QualityFactory.Bands());

        Assert.Equal(2, grouped.Count);
        Assert.All(grouped, reading => Assert.Equal(3, reading.Measures.Count));
        Assert.Equal(1, grouped[0].Subjects);
        Assert.Equal(QualityState.Good, grouped[0].Of(QualityMetric.PacketsLost).State);
        Assert.Equal(QualityState.AtOrAboveWarning, grouped[1].Of(QualityMetric.PacketsLost).State);
    }

    [Fact]
    public void AMeasureAGroupWasNotAskedForIsRefusedRatherThanAnsweredEmpty()
    {
        QualityGroupReading reading = QualityBoard.Grouped(
            [QualityFactory.Row()],
            QualityAxis.Tuner,
            [QualityMetric.PacketsLost],
            QualityFactory.Bands())[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => reading.Of(QualityMetric.Overflows));
    }

    [Fact]
    public void TheWorstGroupComesFirstAndTheOnesNothingMeasuredComeLast()
    {
        IReadOnlyList<QualityGroupReading> sorted = QualityBoard.Sorted(
            QualityBoard.Grouped(
                [
                    QualityFactory.Row(service: 1_024, dropped: 0, total: 1_000_000),
                    QualityFactory.Row(service: 1_032, dropped: 2_000, total: 1_000_000),
                    QualityFactory.Row(service: 1_040, dropped: null, total: null, scrambled: null),
                ],
                QualityAxis.Channel,
                [QualityMetric.PacketsLost],
                QualityFactory.Bands()),
            QualityGroupSort.Worst,
            QualityMetric.PacketsLost,
            ThresholdSense.Ceiling);

        Assert.Equal([1_032, 1_024, 1_040], sorted.Select(reading => reading.Key.Service!.Value));
    }

    [Fact]
    public void TheGroupWithTheMostUnmeasuredComesFirstWhenThatIsWhatWasAsked()
    {
        IReadOnlyList<QualityGroupReading> sorted = QualityBoard.Sorted(
            QualityBoard.Grouped(
                [
                    QualityFactory.Row(service: 1_024, dropped: 0, total: 1_000_000),
                    QualityFactory.Row(service: 1_032, dropped: null, total: null, scrambled: null),
                    QualityFactory.Row(service: 1_032, dropped: null, total: null, scrambled: null),
                ],
                QualityAxis.Channel,
                [QualityMetric.PacketsLost],
                QualityFactory.Bands()),
            QualityGroupSort.Unmeasured,
            QualityMetric.PacketsLost,
            ThresholdSense.Ceiling);

        Assert.Equal([1_032, 1_024], sorted.Select(reading => reading.Key.Service!.Value));
    }

    [Fact]
    public void TheWorstRecordingComesFirstAndTheOnesNothingMeasuredComeLast()
    {
        QualityLedgerRow good = QualityFactory.Row(dropped: 0, total: 1_000_000);
        QualityLedgerRow bad = QualityFactory.Row(dropped: 2_000, total: 1_000_000);
        QualityLedgerRow nothing = QualityFactory.Row(dropped: null, total: null, scrambled: null);

        IReadOnlyList<QualityRowReading> sorted = QualityBoard.Sorted(
            QualitySurvey.Read([good, nothing, bad], [QualityMetric.PacketsLost], QualityFactory.Bands()),
            QualityRecordingSort.Worst,
            QualityMetric.PacketsLost,
            ThresholdSense.Ceiling);

        Assert.Equal(
            [bad.Recording, good.Recording, nothing.Recording],
            sorted.Select(reading => reading.Row.Recording));
    }

    [Fact]
    public void RecordingsComeBackNewestFirstWhenThatIsWhatWasAsked()
    {
        QualityLedgerRow older = QualityFactory.Row(startedAt: new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc));
        QualityLedgerRow newer = QualityFactory.Row(startedAt: new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc));

        IReadOnlyList<QualityRowReading> sorted = QualityBoard.Sorted(
            QualitySurvey.Read([older, newer], [QualityMetric.PacketsLost], QualityFactory.Bands()),
            QualityRecordingSort.StartedAt,
            QualityMetric.PacketsLost,
            ThresholdSense.Ceiling);

        Assert.Equal([newer.Recording, older.Recording], sorted.Select(reading => reading.Row.Recording));
    }

    [Fact]
    public void AnOrderingThisDomainDoesNotNameIsRefused()
    {
        IReadOnlyList<QualityGroupReading> grouped = QualityBoard.Grouped(
            [QualityFactory.Row()],
            QualityAxis.Channel,
            [QualityMetric.PacketsLost],
            QualityFactory.Bands());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            QualityBoard.Sorted(grouped, (QualityGroupSort)99, QualityMetric.PacketsLost, ThresholdSense.Ceiling));
    }
}
