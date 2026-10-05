using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class SignalThresholdMeasureTests
{
    private const double DroppedFrom = 0.0002;

    private static readonly DateTime Monday = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Measured = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SessionSignal TheDriverStarting =
        new("instance-a", Monday, Monday.AddMinutes(1), 0, 10_000_000, 0, null, null);

    [Fact(DisplayName = "BR-QD-023: the carrier to noise floor is the value between the sessions that dropped and the ones that did not")]
    public void TheCarrierToNoiseFloorIsTheValueBetweenTheSessionsThatDroppedAndTheOnesThatDidNot()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(12, turn => Dropped(turn, carrierToNoise: 10_000 + (turn * 400))),
            .. Many(12, turn => Kept(12 + turn, carrierToNoise: 21_600 + (turn * 500))),
        ];

        QualityThresholdMeasurement measurement = Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions);

        Assert.Equal(18_000, measurement.Value);
        Assert.Equal(24, measurement.Sessions);
        Assert.Equal(12, measurement.SessionsDropped);
        Assert.Equal(sessions.Min(session => session.StartedAt), measurement.From);
        Assert.Equal(sessions.Max(session => session.EndedAt), measurement.Until);
        Assert.Equal(Measured, measurement.MeasuredAt);
    }

    [Fact(DisplayName = "BR-QD-023: the bit error rate ceiling is the value between the sessions that dropped and the ones that did not")]
    public void TheBitErrorRateCeilingIsTheValueBetweenTheSessionsThatDroppedAndTheOnesThatDidNot()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, bitErrorRate: 0.003 + (turn * 0.001))),
            .. Many(10, turn => Kept(10 + turn, bitErrorRate: turn * 0.0001)),
        ];

        QualityThresholdMeasurement measurement = Measure(QualityThresholdKey.BitErrorRateCeiling, sessions);

        Assert.Equal(0.002, measurement.Value, 12);
        Assert.Equal(20, measurement.Sessions);
        Assert.Equal(10, measurement.SessionsDropped);
    }

    [Fact(DisplayName = "BR-QD-023: a few sessions on the wrong side do not move the value off the place that misjudges the fewest")]
    public void AFewSessionsOnTheWrongSideDoNotMoveTheValue()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(12, turn => Dropped(turn, carrierToNoise: 10_000 + (turn * 400))),
            Dropped(12, carrierToNoise: 35_000),
            .. Many(12, turn => Kept(13 + turn, carrierToNoise: 21_600 + (turn * 500))),
            Kept(25, carrierToNoise: 9_000),
        ];

        QualityThresholdMeasurement measurement = Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions);

        Assert.Equal(18_000, measurement.Value);
        Assert.Equal(26, measurement.Sessions);
        Assert.Equal(13, measurement.SessionsDropped);
    }

    [Fact(DisplayName = "BR-QD-023: when two places misjudge as few, the one with the wider gap either side is taken")]
    public void WhenTwoPlacesMisjudgeAsFewTheOneWithTheWiderGapIsTaken()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, carrierToNoise: 10_000)),
            Dropped(10, carrierToNoise: 22_000),
            Kept(11, carrierToNoise: 22_000),
            .. Many(10, turn => Kept(12 + turn, carrierToNoise: 30_000)),
        ];

        Assert.Equal(16_000, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).Value);
    }

    [Fact(DisplayName = "BR-QD-023: when two places misjudge as few across gaps as wide, the lower one is taken")]
    public void WhenTwoPlacesMisjudgeAsFewAcrossGapsAsWideTheLowerOneIsTaken()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, carrierToNoise: 10_000)),
            Dropped(10, carrierToNoise: 20_000),
            Kept(11, carrierToNoise: 20_000),
            .. Many(10, turn => Kept(12 + turn, carrierToNoise: 30_000)),
        ];

        Assert.Equal(15_000, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).Value);
    }

    [Fact(DisplayName = "BR-QD-023: the carrier to noise floor is rounded to a tenth of a decibel")]
    public void TheCarrierToNoiseFloorIsRoundedToATenthOfADecibel()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, carrierToNoise: 15_000 - turn)),
            .. Many(10, turn => Kept(10 + turn, carrierToNoise: 15_270 + turn)),
        ];

        Assert.Equal(15_100, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).Value);
    }

    [Fact(DisplayName = "BR-QD-023: the bit error rate ceiling is rounded to two significant figures")]
    public void TheBitErrorRateCeilingIsRoundedToTwoSignificantFigures()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, bitErrorRate: 0.0031 + (turn * 0.001))),
            .. Many(10, turn => Kept(10 + turn, bitErrorRate: 0.0011 - (turn * 0.0001))),
        ];

        Assert.Equal(0.0021, Measure(QualityThresholdKey.BitErrorRateCeiling, sessions).Value, 12);
    }

    [Fact(DisplayName = "BR-QD-023: fewer than ten sessions that dropped, or fewer than ten that did not, decide nothing")]
    public void FewerThanTenOnEitherSideDecideNothing()
    {
        IReadOnlyList<SessionSignal> fewDropped =
        [
            .. Many(9, turn => Dropped(turn, carrierToNoise: 10_000)),
            .. Many(30, turn => Kept(9 + turn, carrierToNoise: 30_000)),
        ];
        IReadOnlyList<SessionSignal> fewKept =
        [
            .. Many(30, turn => Dropped(turn, carrierToNoise: 10_000)),
            .. Many(9, turn => Kept(30 + turn, carrierToNoise: 30_000)),
        ];

        Assert.Null(SignalThresholdMeasure.Measure(QualityThresholdKey.CarrierToNoiseFloor, fewDropped, DroppedFrom, Measured));
        Assert.Null(SignalThresholdMeasure.Measure(QualityThresholdKey.CarrierToNoiseFloor, fewKept, DroppedFrom, Measured));
    }

    [Fact(DisplayName = "BR-QD-023: a value that leaves more than a fifth of either side on the wrong side decides nothing")]
    public void AValueThatLeavesMoreThanAFifthOfEitherSideOnTheWrongSideDecidesNothing()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(20, turn => Dropped(turn, carrierToNoise: 10_000 + (turn * 1_000))),
            .. Many(20, turn => Kept(20 + turn, carrierToNoise: 10_500 + (turn * 1_000))),
        ];

        Assert.Null(SignalThresholdMeasure.Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions, DroppedFrom, Measured));
    }

    [Fact(DisplayName = "BR-QD-023: a session dropped when its share of lost packets reaches the warning level in force")]
    public void ASessionDroppedWhenItsShareReachesTheWarningLevelInForce()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Session(turn, dropped: 2_000, carrierToNoise: 10_000)),
            .. Many(10, turn => Session(10 + turn, dropped: 1_999, carrierToNoise: 30_000)),
        ];

        Assert.Equal(10, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).SessionsDropped);
        Assert.Null(SignalThresholdMeasure.Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions, 0.0003, Measured));
    }

    [Fact(DisplayName = "BR-QD-023: a session that read nothing of a measure is not counted for it")]
    public void ASessionThatReadNothingOfAMeasureIsNotCountedForIt()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, carrierToNoise: 10_000, bitErrorRate: 0.01)),
            .. Many(10, turn => Kept(10 + turn, carrierToNoise: 30_000, bitErrorRate: 0)),
            .. Many(5, turn => Kept(20 + turn, carrierToNoise: null, bitErrorRate: 0)),
        ];

        Assert.Equal(20, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).Sessions);
        Assert.Equal(25, Measure(QualityThresholdKey.BitErrorRateCeiling, sessions).Sessions);
    }

    [Fact(DisplayName = "BR-QD-023: a session that lost packets to the driver's own overflow is left out")]
    public void ASessionThatLostPacketsToAnOverflowIsLeftOut()
    {
        IReadOnlyList<SessionSignal> sessions =
        [
            .. Many(10, turn => Dropped(turn, carrierToNoise: 10_000)),
            .. Many(10, turn => Kept(10 + turn, carrierToNoise: 30_000)),
            Dropped(20, carrierToNoise: 30_000) with { Overflows = 1 },
        ];

        Assert.Equal(20, Measure(QualityThresholdKey.CarrierToNoiseFloor, sessions).Sessions);
    }

    [Fact(DisplayName = "BR-QD-023: the sessions begun in the first ten minutes of a driver are left out")]
    public void TheSessionsBegunInTheFirstTenMinutesOfADriverAreLeftOut()
    {
        DateTime started = Monday.AddDays(1);
        IReadOnlyList<SessionSignal> sessions =
        [
            Kept(0, carrierToNoise: 30_000) with { DriverInstanceId = "later", StartedAt = started, EndedAt = started.AddMinutes(1) },
            Kept(0, carrierToNoise: 30_000) with
            {
                DriverInstanceId = "later",
                StartedAt = started.AddMinutes(9),
                EndedAt = started.AddMinutes(10),
            },
            Kept(0, carrierToNoise: 30_000) with
            {
                DriverInstanceId = "later",
                StartedAt = started.AddMinutes(10),
                EndedAt = started.AddMinutes(11),
            },
        ];

        Assert.Equal(
            [started.AddMinutes(10)],
            SignalThresholdMeasure.Considered(sessions).Select(session => session.StartedAt));
    }

    [Fact(DisplayName = "BR-QD-023: the sessions in the last ten minutes of a driver another one replaced are left out, and the running one keeps its last")]
    public void TheSessionsInTheLastTenMinutesOfAReplacedDriverAreLeftOut()
    {
        DateTime first = Monday;
        DateTime second = Monday.AddDays(1);
        IReadOnlyList<SessionSignal> sessions =
        [
            Run("earlier", first, 0),
            Run("earlier", first, 60),
            Run("earlier", first, 120),
            Run("earlier", first, 125),
            Run("later", second, 0),
            Run("later", second, 60),
        ];

        Assert.Equal(
            [("earlier", first.AddMinutes(60)), ("later", second.AddMinutes(60))],
            SignalThresholdMeasure.Considered(sessions).Select(session => (session.DriverInstanceId, session.StartedAt)));
    }

    [Fact(DisplayName = "BR-QD-023: a finished session is read at what its locked samples usually read, with the worst layer of each sample")]
    public void AFinishedSessionIsReadAtWhatItsLockedSamplesUsuallyRead()
    {
        QualitySessionMeasurement survey = Ended("survey-1", Monday, dropped: 50, total: 1_000_000, overflows: 0);
        QualitySessionMeasurement live = Ended("live-1", Monday.AddHours(1), dropped: 0, total: 1_000_000, overflows: 2);
        QualitySessionMeasurement open = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse("survey-2"),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            Monday.AddHours(2));

        IReadOnlyList<QualitySignalSample> samples =
        [
            Sample("survey-1", Monday.AddSeconds(10), 30_000, 0, 100),
            Sample("survey-1", Monday.AddSeconds(20), 12_000, 5_000, 0),
            Sample("survey-1", Monday.AddSeconds(30), 14_000, 2_000, 0),
            Unlocked("survey-1", Monday.AddSeconds(40)),
            Sample("live-1", Monday.AddHours(1).AddSeconds(10), 25_000, 0, 0),
            Sample("survey-2", Monday.AddHours(2).AddSeconds(10), 25_000, 0, 0),
        ];

        IReadOnlyList<SessionSignal> read = SignalThresholdMeasure.Read([survey, live, open], samples);

        Assert.Equal(2, read.Count);

        SessionSignal first = read[0];

        Assert.Equal("instance-a", first.DriverInstanceId);
        Assert.Equal(Monday, first.StartedAt);
        Assert.Equal(Monday.AddMinutes(1), first.EndedAt);
        Assert.Equal(50, first.DroppedPackets);
        Assert.Equal(1_000_000, first.TotalPackets);
        Assert.Equal(14_000, first.CarrierToNoise);
        Assert.Equal(0.002, first.BitErrorRate!.Value, 12);
        Assert.Equal(2, read[1].Overflows);
        Assert.Equal(25_000, read[1].CarrierToNoise);
    }

    [Fact(DisplayName = "BR-QD-023: a session nothing counted the packets of, or that took no sample, reads no signal")]
    public void ASessionNothingCountedOrThatTookNoSampleReadsNoSignal()
    {
        QualitySessionMeasurement uncounted = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse("survey-1"),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            Monday);

        uncounted.Close(Monday.AddMinutes(1));

        QualitySessionMeasurement unsampled = Ended("survey-2", Monday.AddHours(1), dropped: 0, total: 1_000_000, overflows: 0);

        SessionSignal read = Assert.Single(SignalThresholdMeasure.Read(
            [uncounted, unsampled],
            [Sample("survey-1", Monday.AddSeconds(10), 30_000, 0, 0)]));

        Assert.Null(read.CarrierToNoise);
        Assert.Null(read.BitErrorRate);
    }

    [Fact(DisplayName = "BR-QD-023: only the two signal levels are measured")]
    public void OnlyTheTwoSignalLevelsAreMeasured()
    {
        Assert.Equal(
            [QualityThresholdKey.CarrierToNoiseFloor, QualityThresholdKey.BitErrorRateCeiling],
            SignalThresholdMeasure.Keys);
        Assert.Throws<ArgumentOutOfRangeException>(() => SignalThresholdMeasure.Measure(
            QualityThresholdKey.LockRate,
            [],
            DroppedFrom,
            Measured));
    }

    private static QualityThresholdMeasurement Measure(QualityThresholdKey key, IReadOnlyList<SessionSignal> sessions)
        => SignalThresholdMeasure.Measure(key, [TheDriverStarting, .. sessions], DroppedFrom, Measured)
           ?? throw new InvalidOperationException("The sessions were meant to decide a value.");

    private static IEnumerable<SessionSignal> Many(int count, Func<int, SessionSignal> made)
        => Enumerable.Range(0, count).Select(made);

    private static SessionSignal Dropped(int turn, double? carrierToNoise = 20_000, double? bitErrorRate = 0)
        => Session(turn, dropped: 5_000, carrierToNoise, bitErrorRate);

    private static SessionSignal Kept(int turn, double? carrierToNoise = 20_000, double? bitErrorRate = 0)
        => Session(turn, dropped: 0, carrierToNoise, bitErrorRate);

    private static SessionSignal Session(int turn, long dropped, double? carrierToNoise = 20_000, double? bitErrorRate = 0)
        => new(
            "instance-a",
            Monday.AddHours(1 + turn),
            Monday.AddHours(1 + turn).AddMinutes(1),
            dropped,
            10_000_000,
            0,
            carrierToNoise,
            bitErrorRate);

    private static SessionSignal Run(string instance, DateTime from, int minutes)
        => Kept(0, carrierToNoise: 30_000) with
        {
            DriverInstanceId = instance,
            StartedAt = from.AddMinutes(minutes),
            EndedAt = from.AddMinutes(minutes + 1),
        };

    private static QualitySessionMeasurement Ended(string session, DateTime started, long dropped, long total, long overflows)
    {
        QualitySessionMeasurement measurement = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(session),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            started);

        measurement.Observe(dropped, total, overflows, started.AddMinutes(1));
        measurement.Close(started.AddMinutes(1));

        return measurement;
    }

    private static QualitySignalSample Sample(string session, DateTime at, int carrierToNoise, long firstLayerErrors, long secondLayerErrors)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            SignalSample.WithLock(
                at,
                carrierToNoise,
                at,
                [new LayerBitErrorCounts(0, firstLayerErrors, 1_000_000), new LayerBitErrorCounts(1, secondLayerErrors, 1_000_000)],
                at));

    private static QualitySignalSample Unlocked(string session, DateTime at)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            SignalSample.WithoutLock(at));
}
