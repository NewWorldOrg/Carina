using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Quality;

public sealed class QualitySignalSampleTests
{
    private static readonly DateTime Taken = new(2026, 8, 8, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-QV-003: a stored sample names the session and the driver instance it came from")]
    public void AStoredSampleNamesTheSessionAndTheDriverInstanceItCameFrom()
    {
        QualitySignalSample sample = Sample(SignalSample.WithoutLock(Taken));

        Assert.Equal("driver-7", sample.DriverInstanceId);
        Assert.Equal("survey-1", sample.Session.Value);
        Assert.Equal(SessionPurpose.Survey, sample.Purpose);
    }

    [Fact(DisplayName = "BR-QD-005: a sample with no session could be differenced across a boundary")]
    public void ASampleWithNoSessionCouldBeDifferencedAcrossABoundary()
        => Assert.Throws<ArgumentException>(() => QualitySignalSample.Rehydrate(
            "driver-7",
            default,
            Taken,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            SignalSample.WithoutLock(Taken)));

    [Fact]
    public void ASampleWithNoDriverInstanceCouldBeDifferencedAcrossARestart()
        => Assert.Throws<ArgumentException>(() => QualitySignalSample.Rehydrate(
            " ",
            SessionId.Parse("survey-1"),
            Taken,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            SignalSample.WithoutLock(Taken)));

    [Fact(DisplayName = "BR-QD-013: a sample reaches the channel it was taken on by value")]
    public void ASampleReachesTheChannelItWasTakenOnByValue()
    {
        QualitySignalSample sample = Sample(SignalSample.WithoutLock(Taken));

        Assert.Equal(32736, sample.Network.Value);
        Assert.Equal(1024, sample.Service.Value);
        Assert.Equal("adapter0", sample.Tuner.Value);
    }

    [Fact]
    public void TheTimeASampleWasTakenIsKeptInUtc()
        => Assert.Throws<ArgumentException>(() => QualitySignalSample.Rehydrate(
            "driver-7",
            SessionId.Parse("survey-1"),
            new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Local),
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            SignalSample.WithoutLock(Taken)));

    private static QualitySignalSample Sample(SignalSample signal)
        => QualitySignalSample.Rehydrate(
            "driver-7",
            SessionId.Parse("survey-1"),
            Taken,
            SessionPurpose.Survey,
            new TunerDeviceId("adapter0"),
            new NetworkId(32736),
            new ServiceId(1024),
            signal);
}
