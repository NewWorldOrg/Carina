using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class SignalSampleIntakeTests
{
    private static readonly DateTime Asked = new(2026, 9, 8, 0, 14, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset LockRead = new(2026, 9, 8, 0, 13, 44, TimeSpan.Zero);

    private static readonly DateTimeOffset Measured = new(2026, 9, 8, 0, 13, 43, TimeSpan.Zero);

    [Fact(DisplayName = "BR-QV-003: what the hardware reported is taken as it was reported")]
    public void WhatTheHardwareReportedIsTakenAsItWasReported()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto
            {
                Lock = SignalLock.Locked,
                CnrMilliDecibels = 34779,
                PostViterbiBitErrors =
                [
                    new LayerBitErrorCounts(0, 0, 1671168),
                    new LayerBitErrorCounts(1, 0, 67682304),
                ],
                MeasuredAt = Measured,
                LockReadAt = LockRead,
            },
            Asked);

        Assert.True(read.WasTaken);
        Assert.True(read.Locked);
        Assert.Equal(34779, read.CarrierToNoiseMilliDecibels);
        Assert.Equal(LockRead.UtcDateTime, read.LockReadAt);
        Assert.Equal(Measured.UtcDateTime, read.CarrierToNoiseReadAt);
        Assert.Equal([0, 1], read.BitErrors.Select(counts => counts.Layer));
    }

    [Fact(DisplayName = "BR-QD-004: a frontend that never locked hands over no figure to store")]
    public void AFrontendThatNeverLockedHandsOverNoFigureToStore()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto
            {
                Lock = SignalLock.NotLocked,
                CnrMilliDecibels = -71189,
                MeasuredAt = Measured,
                LockReadAt = LockRead,
            },
            Asked);

        Assert.True(read.WasTaken);
        Assert.False(read.Locked);
        Assert.Null(read.CarrierToNoiseMilliDecibels);
    }

    [Fact(DisplayName = "BR-QV-003: a tuner the driver said nothing about is kept as a reading that could not be taken")]
    public void ATunerTheDriverSaidNothingAboutIsKeptAsAReadingThatCouldNotBeTaken()
    {
        SignalSample read = SignalSampleIntake.Read(null, Asked);

        Assert.Equal(SignalNotTaken.NothingReported, read.NotTakenBecause);
        Assert.Equal(Asked, read.LockReadAt);
    }

    [Fact(DisplayName = "BR-QV-003: a reading with no time on it cannot be told from a frozen one, so it is not taken")]
    public void AReadingWithNoTimeOnItCannotBeToldFromAFrozenOneSoItIsNotTaken()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto { Lock = SignalLock.Locked, CnrMilliDecibels = 33304 },
            Asked);

        Assert.Equal(SignalNotTaken.NoTimeGiven, read.NotTakenBecause);
    }

    [Fact(DisplayName = "BR-QV-003: a figure that arrived without the moment it was measured is not taken either")]
    public void AFigureThatArrivedWithoutTheMomentItWasMeasuredIsNotTakenEither()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto { Lock = SignalLock.Locked, CnrMilliDecibels = 33304, LockReadAt = LockRead },
            Asked);

        Assert.Equal(SignalNotTaken.NoTimeGiven, read.NotTakenBecause);
    }

    [Fact(DisplayName = "BR-QD-009: two counts under one layer would lose which layer failed, so nothing is taken")]
    public void TwoCountsUnderOneLayerWouldLoseWhichLayerFailedSoNothingIsTaken()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto
            {
                Lock = SignalLock.Locked,
                PostViterbiBitErrors = [new LayerBitErrorCounts(0, 1, 8), new LayerBitErrorCounts(0, 2, 8)],
                MeasuredAt = Measured,
                LockReadAt = LockRead,
            },
            Asked);

        Assert.Equal(SignalNotTaken.FiguresRefused, read.NotTakenBecause);
    }

    [Fact(DisplayName = "BR-QD-009: a statistic the tuner does not keep is named on the sample")]
    public void AStatisticTheTunerDoesNotKeepIsNamedOnTheSample()
    {
        SignalSample read = SignalSampleIntake.Read(
            new SignalQualityDto
            {
                Lock = SignalLock.Locked,
                LockReadAt = LockRead,
                NotImplementedMetrics = [SignalQualityMetrics.Cnr],
                MetricsOnAnotherScale = [SignalQualityMetrics.PostViterbiBitError],
            },
            Asked);

        Assert.True(read.WasTaken);
        Assert.Equal(
            [SignalQualityMetrics.Cnr, SignalQualityMetrics.PostViterbiBitError],
            read.MetricsNotRead.Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "BR-QD-005: a sample is filed under the session it was taken during")]
    public void ASampleIsFiledUnderTheSessionItWasTakenDuring()
    {
        QualitySignalSample sample = SignalSampleIntake.Take(
            new SignalReadingAsk(
                "instance-a",
                SessionId.Parse("live-1"),
                SessionPurpose.Live,
                new TunerDeviceId("adapter3.frontend0"),
                new NetworkId(32736),
                new ServiceId(1024),
                new SignalQualityDto { Lock = SignalLock.NotLocked, LockReadAt = LockRead }),
            Asked);

        Assert.Equal("instance-a", sample.DriverInstanceId);
        Assert.Equal("live-1", sample.Session.Value);
        Assert.Equal(Asked, sample.TakenAt);
        Assert.Equal(SessionPurpose.Live, sample.Purpose);
        Assert.Equal("adapter3.frontend0", sample.Tuner.Value);
    }
}
