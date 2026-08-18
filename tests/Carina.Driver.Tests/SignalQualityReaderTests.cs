using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class SignalQualityReaderTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    [Fact]
    public void TheFirstReadingIsTakenWithoutWaitingOutAnInterval()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource();
        var reader = new SignalQualityReader(source, clock, Interval);

        Assert.True(reader.ReadIfDue());
        Assert.Equal(1, source.Reads);
        Assert.NotNull(reader.Latest);
    }

    [Fact]
    public void NoFurtherReadingIsTakenUntilTheIntervalHasPassed()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource();
        var reader = new SignalQualityReader(source, clock, Interval);

        reader.ReadIfDue();
        clock.Advance(Interval - TimeSpan.FromMilliseconds(1));

        Assert.False(reader.ReadIfDue());
        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public void AReadingIsTakenAgainOnceTheIntervalHasPassed()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource();
        var reader = new SignalQualityReader(source, clock, Interval);

        reader.ReadIfDue();
        clock.Advance(Interval);

        Assert.True(reader.ReadIfDue());
        Assert.Equal(2, source.Reads);
        Assert.Equal(Start + Interval, reader.Latest?.MeasuredAt);
    }

    [Fact]
    public void EveryHierarchicalLayerTheFrontendCountsIsKeptApart()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(
            Readings.Measured(
                20.5,
                new LayerBitErrors(0, 12, 1_000_000),
                new LayerBitErrors(1, 3, 500_000)
            )
        );

        SignalQualitySample sample = new SignalQualityReader(source, clock, Interval).Read();

        Assert.Equal(
            [new LayerBitErrors(0, 12, 1_000_000), new LayerBitErrors(1, 3, 500_000)],
            sample.Quality?.PostViterbiErrors.Layers
        );
    }

    [Fact]
    public void ACarrierToNoiseTakenWhileTheFrontendIsNotLockedIsNotAValue()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(Readings.WithoutLock());

        SignalQualitySample sample = new SignalQualityReader(source, clock, Interval).Read();

        Assert.False(sample.HasLock);
        Assert.False(sample.Quality!.CarrierToNoise.TryGetDecibels(out _));
        Assert.Equal(SignalReading.FrontendNotLocked, sample.Quality.CarrierToNoise.Reading);
    }

    [Fact]
    public void TheLockStateIsReadAfterTheStatisticsSoTheTwoCarryTheirOwnTimes()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource(clock)
        {
            ReadingTakes = TimeSpan.FromMilliseconds(4),
        };

        SignalQualitySample sample = new SignalQualityReader(source, clock, Interval).Read();

        Assert.Equal(Start, sample.MeasuredAt);
        Assert.Equal(Start.AddMilliseconds(4), sample.LockReadAt);
    }

    [Fact]
    public void LosingTheLockPartWayThroughASessionIsReportedOnceAndCounted()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(
            Readings.Measured(),
            Readings.WithoutLock(),
            Readings.WithoutLock()
        );
        var losses = new List<SignalQualitySample>();
        var reader = new SignalQualityReader(source, clock, Interval, losses.Add);

        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Single(losses);
        Assert.Equal(1, reader.LockLosses);
        Assert.False(losses[0].HasLock);
    }

    [Fact]
    public void ASessionThatNeverSeesALockedReadingStillReportsTheLossItStartedWith()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(Readings.WithoutLock());
        var losses = new List<SignalQualitySample>();

        new SignalQualityReader(source, clock, Interval, losses.Add).Read();

        Assert.Single(losses);
    }

    [Fact]
    public void ALockThatComesBackAndGoesAgainIsReportedAgain()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(
            Readings.WithoutLock(),
            Readings.Measured(),
            Readings.WithoutLock()
        );
        var losses = new List<SignalQualitySample>();
        var reader = new SignalQualityReader(source, clock, Interval, losses.Add);

        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Equal(2, losses.Count);
        Assert.Equal(2, reader.LockLosses);
    }

    [Fact]
    public void AReadingWhoseTwoStatusesDisagreeIsNotReportedAsALostLock()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(
            Readings.Measured(),
            Readings.Wavering()
        );
        var losses = new List<SignalQualitySample>();
        var reader = new SignalQualityReader(source, clock, Interval, losses.Add);

        reader.Read();
        SignalQualitySample wavering = reader.Read();

        Assert.Empty(losses);
        Assert.Equal(0, reader.LockLosses);
        Assert.False(wavering.HasLock);
        Assert.Equal(SignalReading.UnavailableRightNow, wavering.Quality!.CarrierToNoise.Reading);
    }

    [Fact]
    public void ALossThatFollowsAWaveringReadingIsStillReported()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(
            Readings.Measured(),
            Readings.Wavering(),
            Readings.WithoutLock()
        );
        var losses = new List<SignalQualitySample>();
        var reader = new SignalQualityReader(source, clock, Interval, losses.Add);

        reader.Read();
        reader.Read();
        reader.Read();

        Assert.Single(losses);
    }

    [Fact]
    public void AFrontendThatWillNotAnswerLeavesNoMeasurementAndTellsTheCaller()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource { RefuseFromReadNumber = 1 };
        var problems = new List<Exception>();
        var reader = new SignalQualityReader(source, clock, Interval, problem: problems.Add);

        SignalQualitySample sample = reader.Read();

        Assert.False(sample.Readable);
        Assert.Null(sample.Quality);
        Assert.Single(problems);
        Assert.IsType<DvbDeviceException>(problems[0]);
    }

    [Fact]
    public void AFrontendThatWillNotAnswerIsNotReportedAsALostLock()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource { RefuseFromReadNumber = 2 };
        var losses = new List<SignalQualitySample>();
        var reader = new SignalQualityReader(source, clock, Interval, losses.Add, _ => { });

        reader.Read();
        reader.Read();

        Assert.Empty(losses);
        Assert.Equal(0, reader.LockLosses);
    }

    [Fact]
    public void AReadingThatCouldNotBeTakenStillCountsAsTheIntervalHavingBeenSpent()
    {
        var clock = new ManualTimeProvider(Start);
        var source = new ScriptedQualitySource { RefuseFromReadNumber = 1 };
        var reader = new SignalQualityReader(source, clock, Interval, problem: _ => { });

        reader.ReadIfDue();
        reader.ReadIfDue();

        Assert.Equal(1, source.Reads);
    }

    [Fact]
    public void AMetricThisTunerDoesNotImplementIsNotAMeasurementThatFailed()
    {
        var clock = new ManualTimeProvider(Start);
        ScriptedQualitySource source = new ScriptedQualitySource().Answer(Readings.WithoutCarrierToNoise());

        SignalQualitySample sample = new SignalQualityReader(source, clock, Interval).Read();

        Assert.True(sample.HasLock);
        Assert.Equal(
            SignalReading.NotImplementedByThisTuner,
            sample.Quality!.CarrierToNoise.Reading
        );
        Assert.Equal(SignalReading.Measured, sample.Quality.PostViterbiErrors.Reading);
    }

    [Fact]
    public void AnIntervalThatWouldReadWithoutPauseIsRefusedWhenTheReaderIsBuilt()
    {
        var clock = new ManualTimeProvider(Start);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignalQualityReader(new ScriptedQualitySource(), clock, TimeSpan.Zero)
        );
    }
}
