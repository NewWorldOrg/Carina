using Carina.Contracts;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySurveyTests
{
    [Fact(DisplayName = "a recording nothing counted has no share to read")]
    public void ARecordingNothingCountedHasNoShareToRead()
    {
        QualityLedgerRow row = QualityFactory.Row(dropped: null, total: null, scrambled: null);

        Assert.Null(QualitySurvey.Reading(row, QualityMetric.PacketsLost));
        Assert.Null(QualitySurvey.Reading(row, QualityMetric.PacketsLeftScrambled));
        Assert.Null(QualitySurvey.Reading(row, QualityMetric.Overflows));
    }

    [Fact(DisplayName = "a recording nothing counted is unmeasured, never good")]
    public void ARecordingNothingCountedIsUnmeasuredNeverGood()
    {
        QualityObservation observed = QualitySurvey.Observe(
            QualityFactory.Row(dropped: null, total: null, scrambled: null),
            QualityMetric.PacketsLost,
            QualityFactory.PacketsLost());

        Assert.Equal(QualityStanding.Unmeasured, observed.Standing);
        Assert.Null(observed.Observed);
    }

    [Fact]
    public void ACountedRecordingReadsAsTheShareItLost()
        => Assert.Equal(
            0.0001,
            QualitySurvey.Reading(QualityFactory.Row(dropped: 100, total: 1_000_000), QualityMetric.PacketsLost)!.Value,
            12);

    [Fact]
    public void ACountedRecordingThatCarriedNothingHasNoShareToRead()
        => Assert.Null(QualitySurvey.Reading(QualityFactory.Row(dropped: 0, total: 0), QualityMetric.PacketsLost));

    [Fact]
    public void ARecordingCountedForPacketsButNotForScramblingReadsOnlyTheOne()
    {
        QualityLedgerRow row = QualityFactory.Row(dropped: 100, total: 1_000_000, scrambled: null);

        Assert.NotNull(QualitySurvey.Reading(row, QualityMetric.PacketsLost));
        Assert.Null(QualitySurvey.Reading(row, QualityMetric.PacketsLeftScrambled));
    }

    [Fact]
    public void OverflowsAreCountedOnlyWhereTheSamePassCountedThePackets()
    {
        Assert.Equal(3, QualitySurvey.Reading(QualityFactory.Row(overflows: 3), QualityMetric.Overflows));
        Assert.Null(QualitySurvey.Reading(
            QualityFactory.Row(dropped: null, total: null, overflows: 3),
            QualityMetric.Overflows));
    }

    [Fact(DisplayName = "a recording keeps warning and may-not-be-watchable apart")]
    public void ARecordingKeepsWarningAndMayNotBeWatchableApart()
    {
        IReadOnlyList<QualityRowReading> read = QualitySurvey.Read(
            [
                QualityFactory.Row(dropped: 300, total: 1_000_000),
                QualityFactory.Row(dropped: 2_000, total: 1_000_000),
            ],
            QualityMetrics.All,
            QualityFactory.Bands());

        Assert.Equal(QualityStanding.Warning, read[0].Standing);
        Assert.Equal(QualityStanding.MayNotBeWatchable, read[1].Standing);
        Assert.True(read[1].WentBeyond);
    }

    [Fact]
    public void ARecordingCountedCleanOnEveryMeasureIsGood()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row()],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.Equal(QualityStanding.Good, read.Standing);
        Assert.False(read.WentBeyond);
        Assert.Equal(3, read.Measures.Count);
    }

    [Fact(DisplayName = "a recording measured on one axis and not another is not called good")]
    public void ARecordingMeasuredOnOneAxisAndNotAnotherIsNotCalledGood()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row(dropped: 0, total: 1_000_000, scrambled: null)],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.Equal(QualityStanding.Unmeasured, read.Standing);
    }

    [Fact(DisplayName = "a counted recording holding a gap stands at the warning level however clean its packets")]
    public void ACountedRecordingHoldingAGapStandsAtTheWarningLevel()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row(gaps: 2, missedMs: 3_800)],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.All(read.Measures, measure => Assert.Equal(QualityStanding.Good, measure.Verdict.Standing));
        Assert.Equal(QualityStanding.Warning, read.Gap.Standing);
        Assert.Equal(2, read.Gap.Count);
        Assert.Equal(3_800, read.Gap.MissedMs);
        Assert.Equal(QualityStanding.Warning, read.Standing);
        Assert.True(read.WentBeyond);
    }

    [Fact]
    public void ACountedRecordingHoldingNoGapIsGoodOnItsGaps()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row()],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.Equal(QualityStanding.Good, read.Gap.Standing);
        Assert.Equal(0, read.Gap.Count);
        Assert.Equal(0, read.Gap.MissedMs);
    }

    [Fact(DisplayName = "a recording nothing counted stays unmeasured whatever gaps it holds")]
    public void ARecordingNothingCountedStaysUnmeasuredWhateverGapsItHolds()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row(dropped: null, total: null, scrambled: null, gaps: 1, missedMs: 1_200)],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.Equal(QualityStanding.Unmeasured, read.Gap.Standing);
        Assert.Equal(1, read.Gap.Count);
        Assert.Equal(1_200, read.Gap.MissedMs);
        Assert.Equal(QualityStanding.Unmeasured, read.Standing);
        Assert.False(read.WentBeyond);
    }

    [Fact(DisplayName = "a gap does not soften a recording that may not be watchable")]
    public void AGapDoesNotSoftenARecordingThatMayNotBeWatchable()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row(dropped: 2_000, total: 1_000_000, gaps: 1, missedMs: 500)],
            QualityMetrics.All,
            QualityFactory.Bands())[0];

        Assert.Equal(QualityStanding.MayNotBeWatchable, read.Standing);
    }

    [Fact(DisplayName = "a gap is read whichever measures were asked for")]
    public void AGapIsReadWhicheverMeasuresWereAskedFor()
    {
        QualityRowReading read = QualitySurvey.Read(
            [QualityFactory.Row(gaps: 1, missedMs: 500)],
            [QualityMetric.Overflows],
            QualityFactory.Bands())[0];

        Assert.Single(read.Measures);
        Assert.Equal(QualityStanding.Warning, read.Standing);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void ARowRefusesGapsThatDoNotAddUp(int gaps, long missedMs)
        => Assert.ThrowsAny<ArgumentException>(() => QualityFactory.Row(gaps: gaps, missedMs: missedMs));

    [Fact]
    public void ARecordingTheLedgerCannotPlaceIsStillRead()
    {
        QualityLedgerRow row = QualityFactory.Row(tuner: null, kind: null);

        Assert.Null(row.Facet.Kind);
        Assert.Null(row.Facet.Tuner);
        Assert.Equal(QualityStanding.Good, QualitySurvey.Observe(row, QualityMetric.PacketsLost, QualityFactory.PacketsLost()).Standing);
    }

    [Fact]
    public void ARecordingIsPlacedInTheHourItStarted()
    {
        QualityLedgerRow row = QualityFactory.Row(
            startedAt: new DateTime(2026, 9, 7, 21, 30, 0, DateTimeKind.Utc),
            kind: TuneSystem.IsdbSBs);

        Assert.Equal(21, row.Facet.HourOfDay);
        Assert.Equal(TuneSystem.IsdbSBs, row.Facet.Kind);
    }
}
