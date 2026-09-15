using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Quality;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class SignalSampleRoundTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime TenSecondsOn = Noon.AddSeconds(10);

    private static readonly DateTimeOffset LockRead = new(2026, 9, 8, 11, 59, 58, TimeSpan.Zero);

    private static readonly DateTimeOffset Started = new(2026, 9, 8, 11, 50, 0, TimeSpan.Zero);

    private static readonly TuningParameters Terrestrial = TuningParameters.Terrestrial(27);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QD-004: what the driver reports while it holds a session is kept")]
    public async Task WhatTheDriverReportsWhileItHoldsASessionIsKept()
    {
        HeldQualitySignalSamples samples = new();

        await Round(samples, Held(Quality(34779))).TakeAsync(Cancel);

        QualitySignalSample sample = Assert.Single(samples.Samples);

        Assert.Equal("instance-a", sample.DriverInstanceId);
        Assert.Equal("live-1", sample.Session.Value);
        Assert.Equal("adapter3.frontend0", sample.Tuner.Value);
        Assert.Equal(32736, sample.Network.Value);
        Assert.Equal(1024, sample.Service.Value);
        Assert.Equal(Noon, sample.TakenAt);
        Assert.Equal(34779, sample.Signal.CarrierToNoiseMilliDecibels);
    }

    [Fact(DisplayName = "BR-QD-004: a tuner holding nothing is left alone rather than tuned to be measured")]
    public async Task ATunerHoldingNothingIsLeftAloneRatherThanTunedToBeMeasured()
    {
        HeldQualitySignalSamples samples = new();

        SignalSampleTaking taking = await Round(samples, Idle()).TakeAsync(Cancel);

        Assert.Equal(0, taking.Taken);
        Assert.Empty(samples.Samples);
    }

    [Fact(DisplayName = "BR-QV-003: a tuner the driver said nothing about is kept as one that could not be taken")]
    public async Task ATunerTheDriverSaidNothingAboutIsKeptAsOneThatCouldNotBeTaken()
    {
        HeldQualitySignalSamples samples = new();

        SignalSampleTaking taking = await Round(samples, Held(null)).TakeAsync(Cancel);

        Assert.Equal(1, taking.NotTaken);
        Assert.Equal(SignalNotTaken.NothingReported, Assert.Single(samples.Samples).Signal.NotTakenBecause);
    }

    [Fact]
    public async Task ADriverThatCannotBeReachedLeavesNoSampleBehind()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySessionMeasurements measurements = new();

        SamplingDriverStandIn driver = Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3)));
        driver.Greeting = DriverCall<DriverHello>.Unreachable("the socket is not there");

        await Round(samples, driver, measurements: measurements).TakeAsync(Cancel);

        Assert.Empty(samples.Samples);
        Assert.Empty(measurements.Measurements);
    }

    [Fact(DisplayName = "BR-QD-013: a session on a multiplex no candidate names has no channel to be filed under")]
    public async Task ASessionOnAMultiplexNoCandidateNamesHasNoChannelToBeFiledUnder()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySessionMeasurements measurements = new();

        SignalSampleTaking taking = await Round(
                samples,
                Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3))),
                new HeldStreams([]),
                measurements)
            .TakeAsync(Cancel);

        Assert.Equal(1, taking.Unnamed);
        Assert.Empty(samples.Samples);
        Assert.Empty(measurements.Measurements);
    }

    [Fact(DisplayName = "決定4: what a session that is not a recording counted is kept under that session")]
    public async Task WhatASessionThatIsNotARecordingCountedIsKeptUnderThatSession()
    {
        HeldQualitySessionMeasurements measurements = new();

        SignalSampleTaking taking = await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);

        QualitySessionMeasurement held = Assert.Single(measurements.Measurements);

        Assert.Equal("instance-a", held.DriverInstanceId);
        Assert.Equal("live-1", held.Session.Value);
        Assert.Equal(SessionPurpose.Live, held.Purpose);
        Assert.Equal("adapter3.frontend0", held.Tuner.Value);
        Assert.Equal(32736, held.Network.Value);
        Assert.Equal(1024, held.Service.Value);
        Assert.Equal(Started.UtcDateTime, held.StartedAt);
        Assert.True(held.CcMeasured);
        Assert.Equal(12, held.CcDroppedPackets);
        Assert.Equal(9012, held.CcTotalPackets);
        Assert.Equal(3, held.EovfCount);
        Assert.Equal(Noon, held.MeasuredUpdatedAt);
        Assert.False(held.HasEnded);
        Assert.Equal(1, taking.Measured);
    }

    [Fact(DisplayName = "決定4: what a recording session counted is left to the recording ledger")]
    public async Task WhatARecordingSessionCountedIsLeftToTheRecordingLedger()
    {
        HeldQualitySessionMeasurements measurements = new();

        SignalSampleTaking taking = await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("recording-1", SessionPurpose.Recording, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);

        Assert.Empty(measurements.Measurements);
        Assert.Equal(0, taking.Measured);
    }

    [Fact(DisplayName = "BR-QD-001: a session the driver cannot count is kept as unmeasured rather than as clean")]
    public async Task ASessionTheDriverCannotCountIsKeptAsUnmeasuredRatherThanAsClean()
    {
        HeldQualitySessionMeasurements measurements = new();

        SamplingDriverStandIn driver = Counting(Session("survey-1", SessionPurpose.Survey, Counted(9000, 0, 0)));
        driver.Greeting = DriverCall<DriverHello>.Reached(new DriverHello(DriverProtocol.Version, "instance-a", []));

        await Round(new HeldQualitySignalSamples(), driver, measurements: measurements).TakeAsync(Cancel);

        QualitySessionMeasurement held = Assert.Single(measurements.Measurements);

        Assert.False(held.CcMeasured);
        Assert.Null(held.CcDroppedPackets);
        Assert.Null(held.CcTotalPackets);
        Assert.Null(held.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "BR-QD-005: a later round moves the counts of the same session rather than adding a row")]
    public async Task ALaterRoundMovesTheCountsOfTheSameSessionRatherThanAddingARow()
    {
        HeldQualitySessionMeasurements measurements = new();

        await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("survey-1", SessionPurpose.Survey, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);
        await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("survey-1", SessionPurpose.Survey, Counted(18000, 20, 4))),
                measurements: measurements,
                at: TenSecondsOn)
            .TakeAsync(Cancel);

        QualitySessionMeasurement held = Assert.Single(measurements.Measurements);

        Assert.Equal(20, held.CcDroppedPackets);
        Assert.Equal(18020, held.CcTotalPackets);
        Assert.Equal(4, held.EovfCount);
        Assert.Equal(TenSecondsOn, held.MeasuredUpdatedAt);
    }

    [Fact(DisplayName = "BR-QD-005: a session the driver has concluded is closed with the last counts it gave")]
    public async Task ASessionTheDriverHasConcludedIsClosedWithTheLastCountsItGave()
    {
        HeldQualitySessionMeasurements measurements = new();

        await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("survey-1", SessionPurpose.Survey, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);
        SignalSampleTaking taking = await Round(
                new HeldQualitySignalSamples(),
                Idle(Session("survey-1", SessionPurpose.Survey, Counted(9500, 13, 3), concluded: true)),
                measurements: measurements,
                at: TenSecondsOn)
            .TakeAsync(Cancel);

        QualitySessionMeasurement held = Assert.Single(measurements.Measurements);

        Assert.Equal(TenSecondsOn, held.EndedAt);
        Assert.Equal(13, held.CcDroppedPackets);
        Assert.Equal(9513, held.CcTotalPackets);
        Assert.Equal(1, taking.Closed);
    }

    [Fact(DisplayName = "BR-QD-005: a session the driver no longer lists is closed where it was found gone")]
    public async Task ASessionTheDriverNoLongerListsIsClosedWhereItWasFoundGone()
    {
        HeldQualitySessionMeasurements measurements = new();

        await Round(
                new HeldQualitySignalSamples(),
                Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);

        SamplingDriverStandIn restarted = Idle();
        restarted.Greeting = DriverCall<DriverHello>.Reached(
            new DriverHello(DriverProtocol.Version, "instance-b", [DriverCapabilities.CcMeasurement]));

        SignalSampleTaking taking = await Round(
                new HeldQualitySignalSamples(),
                restarted,
                measurements: measurements,
                at: TenSecondsOn)
            .TakeAsync(Cancel);

        QualitySessionMeasurement held = Assert.Single(measurements.Measurements);

        Assert.Equal(TenSecondsOn, held.EndedAt);
        Assert.Equal(12, held.CcDroppedPackets);
        Assert.Equal(1, taking.Closed);
    }

    [Fact(DisplayName = "BR-QD-007: a session list that did not arrive closes nothing and leaves the signal to be sampled")]
    public async Task ASessionListThatDidNotArriveClosesNothingAndLeavesTheSignalToBeSampled()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySessionMeasurements measurements = new();

        await Round(
                samples,
                Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3))),
                measurements: measurements)
            .TakeAsync(Cancel);

        SamplingDriverStandIn silent = Counting(Session("live-1", SessionPurpose.Live, Counted(9000, 12, 3)));
        silent.Sessions = DriverCall<IReadOnlyList<SessionSnapshot>>.Unreachable("the session list did not arrive");

        await Round(samples, silent, measurements: measurements, at: TenSecondsOn).TakeAsync(Cancel);

        Assert.False(Assert.Single(measurements.Measurements).HasEnded);
        Assert.Equal(2, samples.Samples.Count);
    }

    private static SignalSampleRound Round(
        HeldQualitySignalSamples samples,
        SamplingDriverStandIn driver,
        HeldStreams? streams = null,
        HeldQualitySessionMeasurements? measurements = null,
        DateTime? at = null)
        => new(
            driver,
            streams ?? new HeldStreams(
            [
                new BroadcastStream(
                    new NetworkId(32736),
                    new TransportStreamId(32736),
                    Terrestrial,
                    [new ServiceId(1024), new ServiceId(1025)]),
            ]),
            samples,
            measurements ?? new HeldQualitySessionMeasurements(),
            new HandTurnedClock(at ?? Noon),
            NullLogger<SignalSampleRound>.Instance);

    private static SamplingDriverStandIn Held(SignalQualityDto? quality, SessionId? session = null)
        => new()
        {
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
            [
                new TunerSnapshot("adapter3.frontend0", TunerKind.Terrestrial, TunerState.Busy)
                {
                    CurrentSession = new CurrentSessionDto
                    {
                        SessionId = session ?? SessionId.Parse("live-1"),
                        Purpose = SessionPurpose.Live,
                        Tune = Terrestrial.Typed(),
                    },
                    SignalQuality = quality,
                },
            ]),
        };

    private static SamplingDriverStandIn Counting(SessionSnapshot session)
    {
        SamplingDriverStandIn driver = Held(Quality(34779), session.SessionId);
        driver.Greeting = DriverCall<DriverHello>.Reached(
            new DriverHello(DriverProtocol.Version, "instance-a", [DriverCapabilities.CcMeasurement]));
        driver.Sessions = DriverCall<IReadOnlyList<SessionSnapshot>>.Reached([session]);

        return driver;
    }

    private static SamplingDriverStandIn Idle(params SessionSnapshot[] sessions)
        => new()
        {
            Greeting = DriverCall<DriverHello>.Reached(
                new DriverHello(DriverProtocol.Version, "instance-a", [DriverCapabilities.CcMeasurement])),
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
                [new TunerSnapshot("adapter3.frontend0", TunerKind.Terrestrial, TunerState.Idle)]),
            Sessions = DriverCall<IReadOnlyList<SessionSnapshot>>.Reached(sessions),
        };

    private static SessionSnapshot Session(
        string id,
        SessionPurpose purpose,
        SessionCounters counters,
        bool concluded = false)
        => new(SessionId.Parse(id), purpose, "adapter3.frontend0", concluded ? SessionState.Stopped : SessionState.Active, Started)
        {
            Counters = counters,
            Concluded = concluded,
        };

    private static SessionCounters Counted(long packets, long drops, long overflows)
        => new(Packets: packets, Drops: drops, DeviceOverflows: overflows, CcMeasured: true);

    private static SignalQualityDto Quality(int carrierToNoise)
        => new()
        {
            Lock = SignalLock.Locked,
            CnrMilliDecibels = carrierToNoise,
            MeasuredAt = LockRead,
            LockReadAt = LockRead,
        };
}
