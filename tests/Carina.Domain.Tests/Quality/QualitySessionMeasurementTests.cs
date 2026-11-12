using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySessionMeasurementTests
{
    private static readonly DateTime Started = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "what a session that is not a recording measured has a home of its own")]
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

    [Fact(DisplayName = "what a recording session measured belongs to the recording ledger")]
    public void WhatARecordingSessionMeasuredBelongsToTheRecordingLedger()
        => Assert.Throws<ArgumentException>(() => Open(SessionPurpose.Recording));

    [Fact(DisplayName = "a measurement is kept under the session and the driver it came from")]
    public void AMeasurementIsKeptUnderTheSessionAndTheDriverItCameFrom()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Scan);

        Assert.Equal("driver-7", measurement.DriverInstanceId);
        Assert.Equal("survey-1", measurement.Session.Value);
    }

    [Fact(DisplayName = "a session nobody named cannot be told from the one before it")]
    public void ASessionNobodyNamedCannotBeToldFromTheOneBeforeIt()
        => Assert.Throws<ArgumentException>(() => QualitySessionMeasurement.Open(
            "driver-7",
            default,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started));

    [Fact(DisplayName = "an unmeasured session carries no counts to be read as zero")]
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
        measurement.Observe(2, 750_000, 1, Started.AddMinutes(1));

        Assert.True(measurement.CcMeasured);
        Assert.Equal(2, measurement.CcDroppedPackets);
        Assert.Equal(750_000, measurement.CcTotalPackets);
        Assert.Equal(1, measurement.EovfCount);
        Assert.Equal(Started.AddMinutes(1), measurement.MeasuredUpdatedAt);
    }

    [Fact]
    public void ASessionThatHasEndedKeepsWhatItMeasuredAfterTheSessionIsGone()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);
        measurement.Observe(2, 750_000, 1, Started.AddMinutes(1));
        measurement.Close(Started.AddMinutes(2));

        Assert.True(measurement.HasEnded);
        Assert.Equal(750_000, measurement.CcTotalPackets);
    }

    [Fact(DisplayName = "BR-QD-005: a session closed at a time that reads earlier than its start ends at its start")]
    public void ASessionClosedEarlierThanItsStartEndsAtItsStart()
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);

        measurement.Close(Started.AddTicks(-10));

        Assert.Equal(Started, measurement.EndedAt);
    }

    [Theory(DisplayName = "BR-QD-005: a session closed on the instant it started, or after, ends at the time given")]
    [InlineData(0)]
    [InlineData(10)]
    public void ASessionClosedNoEarlierThanItsStartEndsAtTheTimeGiven(long ticksAfterTheStart)
    {
        QualitySessionMeasurement measurement = Open(SessionPurpose.Survey);

        measurement.Close(Started.AddTicks(ticksAfterTheStart));

        Assert.Equal(Started.AddTicks(ticksAfterTheStart), measurement.EndedAt);
        Assert.True(measurement.HasEnded);
    }

    [Fact]
    public void ARowThatSaysASessionEndedBeforeItStartedIsRefused()
        => Assert.Throws<ArgumentException>(() => QualitySessionMeasurement.Rehydrate(
            "driver-7",
            SessionId.Parse("survey-1"),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            Started,
            Started.AddMinutes(-1),
            false,
            null,
            null,
            0,
            null));

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
