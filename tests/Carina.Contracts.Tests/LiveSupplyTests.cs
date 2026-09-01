using System.Reflection;

namespace Carina.Contracts.Tests;

public sealed class LiveSupplyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SupplyingAStreamToAViewerAddedNoEndpointOfItsOwn()
    {
        Assert.Equal(
            [
                "/health",
                "/tuners",
                "/sessions",
                "/diagnostics",
                "/events",
                "/devices/detected",
                "/tuners/ledger",
                "/restart",
                "/storage",
                "/recordings",
            ],
            DriverEndpoints.All);
    }

    [Fact]
    public void AViewerReadsTheStreamOfASessionRatherThanAStreamOfItsOwn()
    {
        SessionId watched = SessionId.Parse("watched");

        Assert.Equal("/sessions/watched/stream", DriverEndpoints.SessionStream(watched));
        Assert.StartsWith(DriverEndpoints.Session(watched), DriverEndpoints.SessionStream(watched), StringComparison.Ordinal);
        Assert.StartsWith($"{DriverEndpoints.Sessions}/", DriverEndpoints.SessionStream(watched), StringComparison.Ordinal);
    }

    [Fact]
    public void AViewerIsOneOfTheSubscribersTheStreamAlreadyKnows()
    {
        Assert.Equal("as", DriverEndpoints.SubscriberQuery);
        Assert.Equal("viewer", DriverEndpoints.ViewerSubscriber);
        Assert.Equal("piggyback", DriverEndpoints.PiggybackSubscriber);
    }

    [Fact]
    public void ALiveSessionNeedsNoCapabilityBeyondTheBaselineOne()
    {
        Assert.Contains(SessionPurpose.Live, SessionPurposes.Baseline);
        Assert.Null(SessionPurposes.Capability(SessionPurpose.Live));
        Assert.DoesNotContain(
            SessionPurposeConverter.WireName(SessionPurpose.Live),
            string.Join(' ', SessionPurposes.Capabilities),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADriverThatKnowsNothingOfLiveIsNotAskedToDegradeIt()
    {
        Assert.Equal(SessionPurpose.Unspecified, SessionPurposes.Degrades(SessionPurpose.Live));
        Assert.False(SessionPurposes.ReadsEveryPacket(SessionPurpose.Live));
    }

    [Fact]
    public void ALiveRequestIsCompleteWithoutNamingAFileToWrite()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("live-1"),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 27, ServiceId: 1024),
        };

        Assert.Empty(request.Validate(Now));
    }

    [Fact]
    public void ALiveRequestThatNamesAFileToWriteIsRefused()
    {
        var request = new StartSessionRequest
        {
            SessionId = SessionId.Parse("live-1"),
            Purpose = SessionPurpose.Live,
            Tuning = new TuningRequest(TunerKind.Terrestrial, 27, ServiceId: 1024),
            OutputRoot = "primary",
            RecordingId = "k-1",
        };

        Assert.Equal(2, request.Validate(Now).Count);
    }

    [Fact]
    public void ALiveRequestCarriesNoWayToAskForMeasurement()
    {
        string[] asked =
        [
            .. typeof(StartSessionRequest)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(
            ["DeviceId", "EndsAt", "OutputRoot", "Purpose", "RecordingId", "SessionId", "Tune", "Tuning"],
            asked);
    }

    [Fact]
    public void WhatASessionCountsIsReportedBackRatherThanRequested()
    {
        Type[] asks =
        [
            .. typeof(StartSessionRequest)
                .Assembly
                .GetTypes()
                .Where(type => type.IsPublic && type.Name.EndsWith("Request", StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(asks);
        Assert.All(
            asks,
            ask => Assert.DoesNotContain(
                ask.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property => property.PropertyType == typeof(SessionCounters)));

        Assert.Equal(
            typeof(SessionCounters),
            typeof(SessionSnapshot).GetProperty(nameof(SessionSnapshot.Counters))?.PropertyType);
        Assert.False(SessionCounters.Nothing.CcMeasured);
        Assert.False(SessionCounters.Nothing.ScrambleMeasured);
    }
}
