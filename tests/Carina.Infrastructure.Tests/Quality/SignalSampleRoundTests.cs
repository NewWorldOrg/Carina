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

    private static readonly DateTimeOffset LockRead = new(2026, 9, 8, 11, 59, 58, TimeSpan.Zero);

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

        SamplingDriverStandIn driver = new()
        {
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
                [new TunerSnapshot("adapter3.frontend0", TunerKind.Terrestrial, TunerState.Idle)]),
        };

        SignalSampleTaking taking = await Round(samples, driver).TakeAsync(Cancel);

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

        SamplingDriverStandIn driver = Held(Quality(34779));
        driver.Greeting = DriverCall<DriverHello>.Unreachable("the socket is not there");

        await Round(samples, driver).TakeAsync(Cancel);

        Assert.Empty(samples.Samples);
    }

    [Fact(DisplayName = "BR-QD-013: a session on a multiplex no candidate names has no channel to be filed under")]
    public async Task ASessionOnAMultiplexNoCandidateNamesHasNoChannelToBeFiledUnder()
    {
        HeldQualitySignalSamples samples = new();

        SignalSampleTaking taking = await Round(samples, Held(Quality(34779)), new HeldStreams([])).TakeAsync(Cancel);

        Assert.Equal(1, taking.Unnamed);
        Assert.Empty(samples.Samples);
    }

    private static SignalSampleRound Round(
        HeldQualitySignalSamples samples,
        SamplingDriverStandIn driver,
        HeldStreams? streams = null)
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
            new HandTurnedClock(Noon),
            NullLogger<SignalSampleRound>.Instance);

    private static SamplingDriverStandIn Held(SignalQualityDto? quality)
        => new()
        {
            Tuners = DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
            [
                new TunerSnapshot("adapter3.frontend0", TunerKind.Terrestrial, TunerState.Busy)
                {
                    CurrentSession = new CurrentSessionDto
                    {
                        SessionId = SessionId.Parse("live-1"),
                        Purpose = SessionPurpose.Live,
                        Tune = Terrestrial.Typed(),
                    },
                    SignalQuality = quality,
                },
            ]),
        };

    private static SignalQualityDto Quality(int carrierToNoise)
        => new()
        {
            Lock = SignalLock.Locked,
            CnrMilliDecibels = carrierToNoise,
            MeasuredAt = LockRead,
            LockReadAt = LockRead,
        };
}
