using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Quality;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Quality;

public sealed class QualitySignalRollupRoundTests
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QD-006: what the samples of a finished window said is kept in both layers")]
    public async Task WhatTheSamplesOfAFinishedWindowSaidIsKeptInBothLayers()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();

        samples.Samples.Add(Sample(Noon.AddHours(-1)));

        QualitySignalSweep sweep = await Round(samples, rollups).RunAsync(Cancel);

        Assert.Equal(2, sweep.Rolled);
        Assert.Equal(
            [QualityWindow.Minute, QualityWindow.Hour],
            rollups.Rollups.Select(rollup => rollup.Granularity).Order());
    }

    [Fact]
    public async Task RunningTwiceOverTheSameWindowLeavesOneRowPerLayer()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();

        samples.Samples.Add(Sample(Noon.AddHours(-1)));

        await Round(samples, rollups).RunAsync(Cancel);
        await Round(samples, rollups).RunAsync(Cancel);

        Assert.Equal(2, rollups.Rollups.Count);
    }

    [Fact(DisplayName = "BR-QS-003: a sample no window has taken in yet is not deleted")]
    public async Task ASampleNoWindowHasTakenInYetIsNotDeleted()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();

        samples.Samples.Add(Sample(Noon.AddDays(-30)));

        QualitySignalSweep sweep = await Round(samples, rollups, TimeSpan.FromDays(7)).RunAsync(Cancel);

        Assert.Equal(0, sweep.SamplesForgotten);
        Assert.Single(samples.Samples);
    }

    [Fact(DisplayName = "BR-QS-003: a sample past its retention goes once its windows are written")]
    public async Task ASamplePastItsRetentionGoesOnceItsWindowsAreWritten()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();

        samples.Samples.Add(Sample(Noon.AddDays(-30)));

        await Round(samples, rollups, TimeSpan.FromDays(60)).RunAsync(Cancel);

        QualitySignalSweep sweep = await Round(samples, rollups, TimeSpan.FromDays(7)).RunAsync(Cancel);

        Assert.Equal(1, sweep.SamplesForgotten);
        Assert.Empty(samples.Samples);
        Assert.NotEmpty(rollups.Rollups);
    }

    [Fact(DisplayName = "決定2: the minute windows go at their retention and the hourly ones stay")]
    public async Task TheMinuteWindowsGoAtTheirRetentionAndTheHourlyOnesStay()
    {
        HeldQualitySignalSamples samples = new();
        HeldQualitySignalRollups rollups = new();

        samples.Samples.Add(Sample(Noon.AddDays(-120)));

        await Round(samples, rollups, TimeSpan.FromDays(200)).RunAsync(Cancel);

        QualitySignalSweep sweep = await Round(samples, rollups, TimeSpan.FromDays(200)).RunAsync(Cancel);

        Assert.Equal(1, sweep.WindowsForgotten);
        Assert.Equal(QualityWindow.Hour, Assert.Single(rollups.Rollups).Granularity);
    }

    private static QualitySignalRollupRound Round(
        HeldQualitySignalSamples samples,
        HeldQualitySignalRollups rollups,
        TimeSpan? keepSamplesFor = null)
        => new(
            samples,
            rollups,
            new QualitySignalSettings { KeepSamplesFor = keepSamplesFor ?? TimeSpan.FromDays(7) },
            new HandTurnedClock(Noon));

    private static QualitySignalSample Sample(DateTime at)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            SignalSample.WithLock(at, 34779, at));
}
