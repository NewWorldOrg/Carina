using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityTrendTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc);

    private static readonly QualityThresholdHistory AsShipped =
        QualityThresholdHistory.Of(QualityThresholdStanding.Over([], Now), []);

    [Fact(DisplayName = "a period holding no recording gives every point as nothing measured, never as a clean share")]
    public void APeriodHoldingNoRecordingGivesEveryPointAsNothingMeasured()
    {
        QualityTrendFrame frame = Days(7);

        QualityTrendSeries series = QualityTrend.Recordings(frame, QualityMetric.PacketsLost, [], AsShipped);

        Assert.Null(series.Channel);
        Assert.Equal(frame.Buckets.Count, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.Equal(QualityState.NothingToMeasure, point.State);
            Assert.Equal(0, point.Reading.Measured);
            Assert.Null(point.Worst);
        });
    }

    [Fact(DisplayName = "a day whose recordings were none of them measured says unmeasured, and how many")]
    public void ADayWhoseRecordingsWereNoneOfThemMeasuredSaysUnmeasuredAndHowMany()
    {
        QualityTrendFrame frame = Days(3);
        DateTime inside = frame.Buckets[1].From.AddHours(2);

        QualityTrendSeries series = QualityTrend.Recordings(
            frame,
            QualityMetric.PacketsLost,
            [
                QualityFactory.Row(dropped: null, total: null, scrambled: null, startedAt: inside),
                QualityFactory.Row(dropped: null, total: null, scrambled: null, startedAt: inside.AddHours(1)),
            ],
            AsShipped);

        QualityTrendPoint day = series.Points[1];

        Assert.Equal(QualityState.Unmeasured, day.State);
        Assert.Equal(2, day.Reading.Subjects);
        Assert.Equal(2, day.Reading.Unmeasured);
        Assert.Equal(0, day.Reading.Measured);
        Assert.Null(day.Worst);
        Assert.Equal(QualityState.NothingToMeasure, series.Points[0].State);
    }

    [Fact(DisplayName = "the first day past the warning level is where the trend turns")]
    public void TheFirstDayPastTheWarningLevelIsWhereTheTrendTurns()
    {
        QualityTrendFrame frame = Days(5);

        QualityTrendSeries series = QualityTrend.Recordings(
            frame,
            QualityMetric.PacketsLost,
            [
                QualityFactory.Row(dropped: 0, startedAt: frame.Buckets[1].From.AddHours(1)),
                QualityFactory.Row(dropped: 100, startedAt: frame.Buckets[2].From.AddHours(1)),
                QualityFactory.Row(dropped: 500, startedAt: frame.Buckets[3].From.AddHours(1)),
                QualityFactory.Row(dropped: 0, startedAt: frame.Buckets[3].From.AddHours(2)),
                QualityFactory.Row(dropped: 5_000, startedAt: frame.Buckets[4].From.AddHours(1)),
            ],
            AsShipped);

        Assert.Equal(
            [
                QualityState.NothingToMeasure,
                QualityState.Good,
                QualityState.Good,
                QualityState.AtOrAboveWarning,
                QualityState.AtOrAboveWarning,
                QualityState.NothingToMeasure,
            ],
            series.Points.Select(point => point.State));
        Assert.Equal(0.0005, series.Points[3].Worst);
        Assert.Equal(2, series.Points[3].Reading.Measured);
        Assert.Equal(1, series.Points[3].Reading.BeyondThreshold);
        Assert.All(series.Points, point => Assert.Equal(0.0002, point.Level));
    }

    [Fact(DisplayName = "an hour nobody tuned the multiplex is nothing measured, between hours that were")]
    public void AnHourNobodyTunedTheMultiplexIsNothingMeasuredBetweenHoursThatWere()
    {
        QualityTrendFrame frame = Hours();

        IReadOnlyList<QualityTrendSeries> series = QualityTrend.Signal(
            frame,
            QualityThresholdKey.CarrierToNoiseFloor,
            [Window(frame.Buckets[3].From), Window(frame.Buckets[5].From.AddMinutes(20))],
            AsShipped);

        QualityTrendSeries whole = series[0];

        Assert.Null(whole.Channel);
        Assert.Equal(frame.Buckets.Count, whole.Points.Count);
        Assert.Equal(QualityState.Good, whole.Points[3].State);
        Assert.Equal(QualityState.NothingToMeasure, whole.Points[4].State);
        Assert.Equal(0, whole.Points[4].Reading.Measured);
        Assert.Null(whole.Points[4].Worst);
        Assert.Equal(QualityState.Good, whole.Points[5].State);
    }

    [Fact(DisplayName = "an hour whose carrier to noise fell under the floor is past the level")]
    public void AnHourWhoseCarrierToNoiseFellUnderTheFloorIsPastTheLevel()
    {
        QualityTrendFrame frame = Hours();

        QualityTrendSeries whole = QualityTrend.Signal(
            frame,
            QualityThresholdKey.CarrierToNoiseFloor,
            [
                Window(frame.Buckets[0].From),
                Window(frame.Buckets[1].From),
                Window(frame.Buckets[1].From, tuner: "adapter1", cnr: 9_000),
            ],
            AsShipped)[0];

        Assert.Equal(QualityState.Good, whole.Points[0].State);
        Assert.Equal(QualityState.AtOrAboveWarning, whole.Points[1].State);
        Assert.Equal(9_000, whole.Points[1].Worst);
        Assert.Equal(15_000, whole.Points[1].Level);
        Assert.Equal(2, whole.Points[1].Reading.Measured);
        Assert.Equal(1, whole.Points[1].Reading.BeyondThreshold);
    }

    [Fact(DisplayName = "bit errors stay apart by layer in every point")]
    public void BitErrorsStayApartByLayerInEveryPoint()
    {
        QualityTrendFrame frame = Hours();

        QualityTrendPoint point = QualityTrend.Signal(
            frame,
            QualityThresholdKey.BitErrorRateCeiling,
            [
                Window(frame.Buckets[0].From, layers: [new LayerErrorPeak(0, 0.00001), new LayerErrorPeak(1, 0.00002)]),
                Window(frame.Buckets[0].From.AddMinutes(10), layers: [new LayerErrorPeak(1, 0.0003)]),
            ],
            AsShipped)[0].Points[0];

        Assert.Equal([new LayerErrorPeak(0, 0.00001), new LayerErrorPeak(1, 0.0003)], point.Layers);
        Assert.Equal(0.0003, point.Worst);
        Assert.Equal(QualityState.AtOrAboveWarning, point.State);
    }

    [Fact]
    public void TwoTunersOnOneMultiplexMakeOneRowListedAfterTheWhole()
    {
        QualityTrendFrame frame = Hours();

        IReadOnlyList<QualityTrendSeries> series = QualityTrend.Signal(
            frame,
            QualityThresholdKey.LockRate,
            [
                Window(frame.Buckets[0].From, tuner: "adapter0", service: 102),
                Window(frame.Buckets[0].From, tuner: "adapter1", service: 102),
                Window(frame.Buckets[0].From, service: 101),
            ],
            AsShipped);

        Assert.Equal([null, 101, 102], series.Select(row => row.Channel?.Service.Value));
        Assert.Equal(2, series[0].Points[0].Reading.Subjects);
        Assert.Equal(1, series[1].Points[0].Reading.Subjects);
        Assert.Equal(2, series[2].Points[0].Reading.Subjects);
    }

    [Fact(DisplayName = "a tuner that took nothing makes the hour unreachable, and the one that measured is still counted")]
    public void ATunerThatTookNothingMakesTheHourUnreachableAndTheOneThatMeasuredIsStillCounted()
    {
        QualityTrendFrame frame = Hours();

        QualityTrendPoint point = QualityTrend.Signal(
            frame,
            QualityThresholdKey.CarrierToNoiseFloor,
            [
                Window(frame.Buckets[0].From, tuner: "adapter0", locked: 0, unreachable: 360, cnr: null),
                Window(frame.Buckets[0].From, tuner: "adapter1"),
            ],
            AsShipped)[0].Points[0];

        Assert.Equal(QualityState.Unreachable, point.State);
        Assert.Equal(2, point.Reading.Subjects);
        Assert.Equal(1, point.Reading.Measured);
    }

    [Fact(DisplayName = "each day is judged against the level that stood when it closed")]
    public void EachDayIsJudgedAgainstTheLevelThatStoodWhenItClosed()
    {
        QualityTrendFrame frame = Days(5);

        QualityThresholdHistory levels = QualityThresholdHistory.Of(
            QualityThresholdStanding.Over([], Now),
            [
                QualityThresholdChange.Record(
                    QualityThresholdChangeId.New(),
                    QualityThresholdKey.PacketsLostWarning,
                    0.0005,
                    0.0002,
                    frame.Buckets[2].From.AddHours(6),
                    null),
            ]);

        QualityTrendSeries series = QualityTrend.Recordings(
            frame,
            QualityMetric.PacketsLost,
            [
                QualityFactory.Row(dropped: 300, startedAt: frame.Buckets[1].From.AddHours(1)),
                QualityFactory.Row(dropped: 300, startedAt: frame.Buckets[3].From.AddHours(1)),
            ],
            levels);

        Assert.Equal(QualityState.Good, series.Points[1].State);
        Assert.Equal(0.0005, series.Points[1].Level);
        Assert.Equal(QualityState.AtOrAboveWarning, series.Points[3].State);
        Assert.Equal(0.0002, series.Points[3].Level);
    }

    private static QualityTrendFrame Days(int days) => QualityTrendFrame.Over(days, Now, QualityTrendStep.Day)!;

    private static QualityTrendFrame Hours() => QualityTrendFrame.Over(1, Now, QualityTrendStep.Hour)!;

    private static QualitySignalWindow Window(
        DateTime start,
        string tuner = "adapter0",
        int service = 101,
        long locked = 360,
        long unreachable = 0,
        int? cnr = 30_000,
        IReadOnlyList<LayerErrorPeak>? layers = null)
        => new(
            start,
            new TunerDeviceId(tuner),
            new NetworkId(1),
            new ServiceId(service),
            360,
            locked,
            0,
            unreachable,
            cnr,
            layers ?? [],
            [],
            cnr is not null || layers is { Count: > 0 } ? start : null);
}
