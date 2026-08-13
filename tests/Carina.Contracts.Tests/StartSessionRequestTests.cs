namespace Carina.Contracts.Tests;

public sealed class StartSessionRequestTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 21, 4, 0, TimeSpan.FromHours(9));

    private static StartSessionRequest Request(
        SessionPurpose purpose = SessionPurpose.Live,
        TunerKind kind = TunerKind.Terrestrial,
        int physicalChannel = 27,
        int? serviceId = null,
        DateTimeOffset? endsAt = null,
        string? deviceId = null
    ) =>
        new()
        {
            Purpose = purpose,
            Tuning = new TuningRequest(kind, physicalChannel, serviceId),
            EndsAt = endsAt,
            DeviceId = deviceId,
        };

    [Fact]
    public void AnOrdinaryRequestHasNothingWrongWithIt()
    {
        Assert.Empty(Request().Validate(Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(256)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void APhysicalChannelNoDeviceCouldTakeIsReported(int channel)
    {
        Assert.Contains(
            Request(physicalChannel: channel).Validate(Now),
            problem => problem.StartsWith("tuning.physicalChannel:")
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(27)]
    [InlineData(255)]
    public void APhysicalChannelInsideTheBoundIsAccepted(int channel)
    {
        Assert.Empty(Request(physicalChannel: channel).Validate(Now));
    }

    [Theory]
    [InlineData("../../../dev/mem")]
    [InlineData("adapter0;reboot")]
    [InlineData("")]
    [InlineData("a/b")]
    public void ADeviceNameOutsideTheShapeIsReported(string deviceId)
    {
        Assert.Contains(
            Request(deviceId: deviceId).Validate(Now),
            problem => problem.StartsWith("deviceId:")
        );
    }

    [Theory]
    [InlineData("adapter0")]
    [InlineData("dvb0.frontend0")]
    [InlineData("tuner_1")]
    public void AnOrdinaryDeviceNameIsAccepted(string deviceId)
    {
        Assert.Empty(Request(deviceId: deviceId).Validate(Now));
    }

    [Fact]
    public void ARecordingEndingInThePastIsReported()
    {
        Assert.Contains(
            Request(
                purpose: SessionPurpose.Recording,
                endsAt: Now.AddMinutes(-1)
            ).Validate(Now),
            problem => problem.StartsWith("endsAt:")
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void AServiceIdOutsideSixteenBitsIsReported(int serviceId)
    {
        Assert.Contains(
            Request(serviceId: serviceId).Validate(Now),
            problem => problem.StartsWith("tuning.serviceId:")
        );
    }

    [Fact]
    public void APurposeThisDriverDoesNotKnowIsReported()
    {
        Assert.Contains(
            Request(purpose: SessionPurpose.Unspecified).Validate(Now),
            problem => problem.StartsWith("purpose:")
        );
    }

    [Fact]
    public void ATunerKindThisDriverDoesNotKnowIsReported()
    {
        Assert.Contains(
            Request(kind: TunerKind.Unspecified).Validate(Now),
            problem => problem.StartsWith("tuning.kind:")
        );
    }

    [Fact]
    public void ARecordingWithoutAnEndTimeIsReported()
    {
        Assert.Contains(
            Request(purpose: SessionPurpose.Recording).Validate(Now),
            problem => problem.StartsWith("endsAt:")
        );
    }

    [Fact]
    public void ARecordingWithAnEndTimeAheadIsAccepted()
    {
        Assert.Empty(
            Request(purpose: SessionPurpose.Recording, endsAt: Now.AddHours(1)).Validate(Now)
        );
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var problems = Request(
            purpose: SessionPurpose.Unspecified,
            kind: TunerKind.Unspecified,
            serviceId: -5,
            deviceId: "../x"
        ).Validate(Now);

        Assert.Equal(4, problems.Count);
    }
}
