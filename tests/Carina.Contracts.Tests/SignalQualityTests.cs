namespace Carina.Contracts.Tests;

public sealed class SignalQualityTests
{
    private static readonly DateTimeOffset Moment =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static SignalQualityDto Locked =>
        new()
        {
            Lock = SignalLock.Locked,
            CnrMilliDecibels = 21_500,
            PostViterbiBitErrors =
            [
                new LayerBitErrorCounts(0, 12, 1_000_000),
                new LayerBitErrorCounts(1, 0, 500_000),
            ],
            MeasuredAt = Moment,
        };

    [Fact]
    public void ALockedReadingCarriesWhatWasMeasured()
    {
        Assert.Equal(21_500, Locked.CnrMilliDecibels);
        Assert.Equal(21.5m, Locked.CnrDecibels);
        Assert.Equal(2, Locked.PostViterbiBitErrors.Count);
    }

    [Fact]
    public void TheCarrierToNoiseOfATunerThatIsNotLockedIsNotAMeasurement()
    {
        var reading = Locked with { Lock = SignalLock.NotLocked };

        Assert.Null(reading.CnrMilliDecibels);
        Assert.Null(reading.CnrDecibels);
        Assert.DoesNotContain("21500", DriverJson.Serialize(reading), StringComparison.Ordinal);
    }

    [Fact]
    public void ALockStateThisBuildDoesNotKnowIsNotReadAsLocked()
    {
        var reading = DriverJson.Deserialize(
            """{"lock":"almostThere","cnrMilliDecibels":21500,"postViterbiBitErrors":[{"layer":0,"errorBits":12,"totalBits":1000000}]}""",
            DriverJson.Context.SignalQualityDto
        );

        Assert.NotNull(reading);
        Assert.Equal(SignalLock.Unspecified, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Empty(reading.PostViterbiBitErrors);
    }

    [Fact]
    public void AReadingWithoutALockStateIsNotReadAsLocked()
    {
        var reading = DriverJson.Deserialize("{}", DriverJson.Context.SignalQualityDto);

        Assert.NotNull(reading);
        Assert.Equal(SignalLock.Unspecified, reading.Lock);
        Assert.Null(reading.CnrMilliDecibels);
        Assert.Empty(reading.PostViterbiBitErrors);
        Assert.Null(reading.MeasuredAt);
    }

    [Fact]
    public void TheLayersOfATerrestrialReadingAreKeptApart()
    {
        var restored = DriverJson.Deserialize(
            DriverJson.Serialize(Locked),
            DriverJson.Context.SignalQualityDto
        );

        Assert.NotNull(restored);
        Assert.Equal(
            [new LayerBitErrorCounts(0, 12, 1_000_000), new LayerBitErrorCounts(1, 0, 500_000)],
            restored.PostViterbiBitErrors
        );
    }

    [Fact]
    public void ALayerCountThisBuildDoesNotExpectIsCarriedAsItStands()
    {
        var reading = DriverJson.Deserialize(
            """{"lock":"locked","postViterbiBitErrors":[{"layer":0,"errorBits":1,"totalBits":2},{"layer":1,"errorBits":3,"totalBits":4},{"layer":2,"errorBits":5,"totalBits":6}]}""",
            DriverJson.Context.SignalQualityDto
        );

        Assert.NotNull(reading);
        Assert.Equal(3, reading.PostViterbiBitErrors.Count);
        Assert.Equal(2, reading.PostViterbiBitErrors[2].Layer);
    }

    [Fact]
    public void ASatelliteReadingHasTheOneLayerItMeasures()
    {
        var reading = new SignalQualityDto
        {
            Lock = SignalLock.Locked,
            CnrMilliDecibels = 11_200,
            PostViterbiBitErrors = [new LayerBitErrorCounts(0, 0, 2_000_000)],
        };

        Assert.Single(reading.PostViterbiBitErrors);
    }

    [Theory]
    [InlineData(0, 1_000, 0d)]
    [InlineData(5, 1_000, 0.005d)]
    public void ALayerReportsTheRateItsCountersImply(long errors, long total, double rate)
    {
        Assert.Equal(rate, new LayerBitErrorCounts(0, errors, total).ErrorRate);
    }

    [Fact]
    public void ALayerThatCountedNothingHasNoRate()
    {
        Assert.Null(new LayerBitErrorCounts(0, 0, 0).ErrorRate);
    }

    [Fact]
    public void ACountersOnlyReadingIsNotMistakenForARate()
    {
        var json = DriverJson.Serialize(Locked);

        Assert.Contains("\"errorBits\":12", json, StringComparison.Ordinal);
        Assert.Contains("\"totalBits\":1000000", json, StringComparison.Ordinal);
        Assert.DoesNotContain("errorRate", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SignalStrengthIsNotPartOfWhatIsReported()
    {
        Assert.DoesNotContain("strength", DriverJson.Serialize(Locked), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CountersAreNeverNull()
    {
        Assert.Empty(
            new SignalQualityDto { Lock = SignalLock.Locked, PostViterbiBitErrors = null! }
                .PostViterbiBitErrors
        );
    }
}
