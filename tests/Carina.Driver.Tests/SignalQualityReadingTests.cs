using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class SignalQualityReadingTests
{
    private const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    private const FrontendStatus CarrierOnly = FrontendStatus.Signal;

    [Fact]
    public void ALockedFrontendReportsItsCarrierToNoiseInDecibels()
    {
        var reading = SignalQualityReading.CarrierToNoiseFrom(
            Locked,
            [new DvbStatisticLayer(StatisticScale.Decibel, 21_500)]
        );

        Assert.Equal(SignalReading.Measured, reading.Reading);
        Assert.True(reading.TryGetDecibels(out var decibels));
        Assert.Equal(21.5, decibels, 3);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(-71_189)]
    [InlineData(-33_674)]
    public void AnUnlockedFrontendsPlausibleLookingCarrierToNoiseIsNotAMeasurement(
        long millidecibels
    )
    {
        var reading = SignalQualityReading.CarrierToNoiseFrom(
            CarrierOnly,
            [new DvbStatisticLayer(StatisticScale.Decibel, millidecibels)]
        );

        Assert.Equal(SignalReading.FrontendNotLocked, reading.Reading);
        Assert.False(reading.TryGetDecibels(out var decibels));
        Assert.True(double.IsNaN(decibels));
    }

    [Fact]
    public void ATunerThatDoesNotImplementCarrierToNoiseSaysSoEvenWhileUnlocked()
    {
        var reading = SignalQualityReading.CarrierToNoiseFrom(CarrierOnly, []);

        Assert.Equal(SignalReading.NotImplementedByThisTuner, reading.Reading);
        Assert.False(reading.TryGetDecibels(out _));
    }

    [Fact]
    public void ACarrierToNoiseTheDriverMarksUnavailableIsNotTreatedAsZeroDecibels()
    {
        var reading = SignalQualityReading.CarrierToNoiseFrom(
            Locked,
            [new DvbStatisticLayer(StatisticScale.NotAvailable, 0)]
        );

        Assert.Equal(SignalReading.UnavailableRightNow, reading.Reading);
        Assert.False(reading.TryGetDecibels(out _));
    }

    [Fact]
    public void ACarrierToNoiseOnARelativeScaleIsNotReadAsThoughItWereDecibels()
    {
        var reading = SignalQualityReading.CarrierToNoiseFrom(
            Locked,
            [new DvbStatisticLayer(StatisticScale.Relative, 40_000)]
        );

        Assert.Equal(SignalReading.UnavailableRightNow, reading.Reading);
        Assert.False(reading.TryGetDecibels(out _));
    }

    [Fact]
    public void ADefaultCarrierToNoiseYieldsNoMeasurement()
    {
        Assert.False(default(CarrierToNoise).TryGetDecibels(out var decibels));
        Assert.True(double.IsNaN(decibels));
        Assert.Equal(SignalReading.Unspecified, default(CarrierToNoise).Reading);
    }

    [Fact]
    public void BothTerrestrialLayersKeepTheirOwnErrorCounters()
    {
        var errors = SignalQualityReading.PostViterbiFrom(
            Locked,
            [
                new DvbStatisticLayer(StatisticScale.Counter, 12),
                new DvbStatisticLayer(StatisticScale.Counter, 7),
            ],
            [
                new DvbStatisticLayer(StatisticScale.Counter, 1_000_000),
                new DvbStatisticLayer(StatisticScale.Counter, 2_000_000),
            ]
        );

        Assert.Equal(SignalReading.Measured, errors.Reading);
        Assert.Equal(2, errors.Layers.Count);
        Assert.Equal(new LayerBitErrors(0, 12, 1_000_000), errors.Layers[0]);
        Assert.Equal(new LayerBitErrors(1, 7, 2_000_000), errors.Layers[1]);
    }

    [Fact]
    public void ALayerErrorRateIsErrorBitsOverTotalBits()
    {
        Assert.True(new LayerBitErrors(0, 12, 1_000_000).TryGetErrorRate(out var rate));
        Assert.Equal(1.2e-5, rate, 12);
    }

    [Fact]
    public void ALayerThatCountedNoBitsAtAllHasNoErrorRate()
    {
        Assert.False(new LayerBitErrors(0, 0, 0).TryGetErrorRate(out var rate));
        Assert.True(double.IsNaN(rate));
    }

    [Fact]
    public void AnUnlockedFrontendsErrorCountersAreNotAMeasurement()
    {
        var errors = SignalQualityReading.PostViterbiFrom(
            CarrierOnly,
            [new DvbStatisticLayer(StatisticScale.Counter, 999)],
            [new DvbStatisticLayer(StatisticScale.Counter, 1_000)]
        );

        Assert.Equal(SignalReading.FrontendNotLocked, errors.Reading);
        Assert.Empty(errors.Layers);
    }

    [Fact]
    public void ATunerThatCountsNoBitsAtAllSaysItDoesNotImplementTheCounters()
    {
        var errors = SignalQualityReading.PostViterbiFrom(CarrierOnly, [], []);

        Assert.Equal(SignalReading.NotImplementedByThisTuner, errors.Reading);
    }

    [Fact]
    public void MismatchedLayerCountsAreRefusedRatherThanPairedUpToTheShorterOne()
    {
        var errors = SignalQualityReading.PostViterbiFrom(
            Locked,
            [
                new DvbStatisticLayer(StatisticScale.Counter, 12),
                new DvbStatisticLayer(StatisticScale.Counter, 7),
            ],
            [new DvbStatisticLayer(StatisticScale.Counter, 1_000_000)]
        );

        Assert.Equal(SignalReading.UnavailableRightNow, errors.Reading);
        Assert.Empty(errors.Layers);
    }

    [Fact]
    public void CountersOnAScaleThatIsNotACountAreRefused()
    {
        var errors = SignalQualityReading.PostViterbiFrom(
            Locked,
            [new DvbStatisticLayer(StatisticScale.Decibel, 12)],
            [new DvbStatisticLayer(StatisticScale.Counter, 1_000_000)]
        );

        Assert.Equal(SignalReading.UnavailableRightNow, errors.Reading);
        Assert.Empty(errors.Layers);
    }

    [Fact]
    public void TheLockFlagIsWhatDecidesWhetherAnythingWasMeasured()
    {
        Assert.True(new SignalQuality(Locked, default, PostViterbiErrors.None).HasLock);
        Assert.False(new SignalQuality(CarrierOnly, default, PostViterbiErrors.None).HasLock);
        Assert.False(new SignalQuality(FrontendStatus.None, default, PostViterbiErrors.None).HasLock);
    }

    [Fact]
    public void ATimedOutFrontendIsNotLocked()
    {
        Assert.False(
            new SignalQuality(FrontendStatus.TimedOut, default, PostViterbiErrors.None).HasLock
        );
    }
}
