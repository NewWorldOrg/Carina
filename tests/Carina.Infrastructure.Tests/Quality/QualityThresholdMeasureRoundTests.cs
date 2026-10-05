using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class QualityThresholdMeasureRoundTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HandTurnedClock clock = new(Now);

    private readonly HeldQualityThresholds thresholds = new();

    private readonly HeldQualityThresholdChanges changes = new();

    private readonly HeldQualitySessionMeasurements sessions = new();

    private readonly HeldQualitySignalSamples samples = new();

    private readonly SilentEvents events = new();

    [Fact(DisplayName = "BR-QD-023: the two signal levels are measured from the sessions of the last seven days, recorded as changed by a measurement, and the screen is told")]
    public async Task TheTwoSignalLevelsAreMeasuredFromTheSessionsOfTheLastSevenDays()
    {
        await SessionsAsync(dropped: 12, kept: 12);

        QualityThresholdMeasurePass pass = await Round().RunAsync(Cancel);

        Assert.Equal(2, pass.Measured);
        Assert.Equal(2, pass.Moved);

        QualityThreshold carrierToNoise = Held(QualityThresholdKey.CarrierToNoiseFloor);
        QualityThreshold bitErrors = Held(QualityThresholdKey.BitErrorRateCeiling);

        Assert.Equal(20_000, carrierToNoise.Setting.Current);
        Assert.False(carrierToNoise.Setting.Provisional);
        Assert.Equal(24, carrierToNoise.Measurement!.Sessions);
        Assert.Equal(12, carrierToNoise.Measurement.SessionsDropped);
        Assert.Equal(Now, carrierToNoise.Measurement.MeasuredAt);
        Assert.Equal(0.0025, bitErrors.Setting.Current, 12);
        Assert.All(changes.Changes, change => Assert.Equal(QualityThresholdChangeCause.Measurement, change.Cause));
        Assert.Equal(2, changes.Changes.Count);
        Assert.Equal([AppEventName.Quality], events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: sessions begun before the last seven days are not read")]
    public async Task SessionsBegunBeforeTheLastSevenDaysAreNotRead()
    {
        await SessionsAsync(dropped: 12, kept: 12, startingAt: Now.AddDays(-9));

        QualityThresholdMeasurePass pass = await Round().RunAsync(Cancel);

        Assert.Equal(0, pass.Measured);
        Assert.Empty(thresholds.Thresholds);
        Assert.Empty(events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: too few sessions leave the levels as they are, and say nothing")]
    public async Task TooFewSessionsLeaveTheLevelsAsTheyAre()
    {
        await SessionsAsync(dropped: 5, kept: 12);

        QualityThresholdMeasurePass pass = await Round().RunAsync(Cancel);

        Assert.Equal(0, pass.Measured);
        Assert.Empty(thresholds.Thresholds);
        Assert.Empty(changes.Changes);
        Assert.Empty(events.Signalled);
    }

    [Fact(DisplayName = "BR-QD-023: a level set by hand keeps its value and is given the measurement beside it")]
    public async Task ALevelSetByHandKeepsItsValue()
    {
        await thresholds.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.CarrierToNoiseFloor,
                Threshold.Of(15_000, 12_000, provisional: true, 0, Now.AddDays(-1)),
                null,
                byHand: true,
                null),
            Cancel);
        await SessionsAsync(dropped: 12, kept: 12);

        QualityThresholdMeasurePass pass = await Round().RunAsync(Cancel);

        QualityThreshold carrierToNoise = Held(QualityThresholdKey.CarrierToNoiseFloor);

        Assert.Equal(2, pass.Measured);
        Assert.Equal(1, pass.Moved);
        Assert.Equal(12_000, carrierToNoise.Setting.Current);
        Assert.True(carrierToNoise.ByHand);
        Assert.Equal(20_000, carrierToNoise.Measurement!.Value);
        Assert.DoesNotContain(changes.Changes, change => change.Key == QualityThresholdKey.CarrierToNoiseFloor);
    }

    [Fact(DisplayName = "BR-QD-023: a session drops when its share of lost packets reaches the drop warning level in force")]
    public async Task ASessionDropsAgainstTheDropWarningLevelInForce()
    {
        await thresholds.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.PacketsLostWarning,
                Threshold.Of(0.0002, 0.9, provisional: true, 0, Now.AddDays(-1)),
                null,
                byHand: true,
                null),
            Cancel);
        await SessionsAsync(dropped: 12, kept: 12);

        QualityThresholdMeasurePass pass = await Round().RunAsync(Cancel);

        Assert.Equal(0, pass.Measured);
    }

    private QualityThreshold Held(QualityThresholdKey key) => thresholds.Thresholds.Single(threshold => threshold.Key == key);

    private QualityThresholdMeasureRound Round()
        => new(
            thresholds,
            changes,
            sessions,
            samples,
            new UnguardedWrites(),
            events,
            new QualitySignalSettings(),
            clock,
            NullLogger<QualityThresholdMeasureRound>.Instance);

    private async Task SessionsAsync(int dropped, int kept, DateTime? startingAt = null)
    {
        DateTime first = startingAt ?? Now.AddDays(-6);

        await SessionAsync("start", first, lost: 0, carrierToNoise: 30_000, errorBits: 0);

        for (int turn = 0; turn < dropped; turn++)
        {
            await SessionAsync($"dropped-{turn}", first.AddHours(1 + turn), lost: 5_000, carrierToNoise: 12_000, errorBits: 5_000);
        }

        for (int turn = 0; turn < kept; turn++)
        {
            await SessionAsync($"kept-{turn}", first.AddHours(1 + dropped + turn), lost: 0, carrierToNoise: 28_000, errorBits: 0);
        }
    }

    private async Task SessionAsync(string id, DateTime started, long lost, int carrierToNoise, long errorBits)
    {
        QualitySessionMeasurement session = QualitySessionMeasurement.Open(
            "instance-a",
            SessionId.Parse(id),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            started);

        session.Observe(lost, 10_000_000, 0, started.AddMinutes(1));
        session.Close(started.AddMinutes(1));
        await sessions.SaveAsync(session, Cancel);
        samples.Samples.Add(QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(id),
            started.AddSeconds(10),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32_736),
            new ServiceId(1_024),
            SignalSample.WithLock(
                started.AddSeconds(10),
                carrierToNoise,
                started.AddSeconds(10),
                [new LayerBitErrorCounts(1, errorBits, 1_000_000)],
                started.AddSeconds(10))));
    }
}
