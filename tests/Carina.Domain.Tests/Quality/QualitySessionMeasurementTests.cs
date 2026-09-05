using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySessionMeasurementTests
{
    private static readonly DateTime Started = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "決定4: what a session that is not a recording measured has a home of its own")]
    public void WhatASessionThatIsNotARecordingMeasuredHasAHomeOfItsOwn()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);

        Assert.Equal(SessionPurpose.Survey, measurement.Purpose);
        Assert.False(measurement.CcMeasured);
        Assert.Null(measurement.CcDroppedPackets);
        Assert.Null(measurement.CcTotalPackets);
        Assert.Null(measurement.MeasuredUpdatedAt);
        Assert.False(measurement.HasEnded);
    }

    [Fact(DisplayName = "決定4: what a recording session measured belongs to the recording ledger")]
    public void WhatARecordingSessionMeasuredBelongsToTheRecordingLedger()
        => Assert.Throws<ArgumentException>(() => Open(SessionPurpose.Recording));

    [Fact(DisplayName = "BR-QD-005: a measurement is kept under the session and the driver it came from")]
    public void AMeasurementIsKeptUnderTheSessionAndTheDriverItCameFrom()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Scan);

        Assert.Equal("driver-7", measurement.DriverInstanceId);
        Assert.Equal("survey-1", measurement.Session.Value);
    }

    [Fact(DisplayName = "BR-QD-005: a session nobody named cannot be told from the one before it")]
    public void ASessionNobodyNamedCannotBeToldFromTheOneBeforeIt()
        => Assert.Throws<ArgumentException>(() => QualitySessionMeasurement.Open(
            "driver-7",
            default,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started));

    [Fact(DisplayName = "BR-QD-001: an unmeasured session carries no counts to be read as zero")]
    public void AnUnmeasuredSessionCarriesNoCountsToBeReadAsZero()
        => Assert.Throws<ArgumentException>(() => QualitySessionMeasurement.Rehydrate(
            "driver-7",
            SessionId.Parse("survey-1"),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started,
            null,
            false,
            0,
            0,
            0,
            null));

    [Fact]
    public void ASessionThatHasBeenCountedSaysWhenItWasLastCounted()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);
        measurement.Observe(2, 741375, 1, Started.AddMinutes(1));

        Assert.True(measurement.CcMeasured);
        Assert.Equal(2, measurement.CcDroppedPackets);
        Assert.Equal(741375, measurement.CcTotalPackets);
        Assert.Equal(1, measurement.EovfCount);
        Assert.Equal(Started.AddMinutes(1), measurement.MeasuredUpdatedAt);
    }

    [Fact]
    public void ASessionThatHasEndedKeepsWhatItMeasuredAfterTheSessionIsGone()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);
        measurement.Observe(2, 741375, 1, Started.AddMinutes(1));
        measurement.Close(Started.AddMinutes(2));

        Assert.True(measurement.HasEnded);
        Assert.Equal(741375, measurement.CcTotalPackets);
    }

    [Fact]
    public void ASessionDoesNotEndBeforeItStarts()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);

        Assert.Throws<ArgumentException>(() => measurement.Close(Started.AddMinutes(-1)));
    }

    private static QualitySessionMeasurement Open(SessionPurpose purpose)
        => QualitySessionMeasurement.Open(
            "driver-7",
            SessionId.Parse("survey-1"),
            purpose,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started);
}
