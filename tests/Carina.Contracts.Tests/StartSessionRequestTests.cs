namespace Carina.Contracts.Tests;

/// <summary>
/// What the driver checks before a value reaches a device.
/// </summary>
/// <remarks>
/// The request crosses a process boundary into the privileged side, so the numbers
/// are checked rather than trusted. The check reports rather than throws: a bad
/// request has to become an answer, not a crash.
/// </remarks>
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
    [InlineData(12)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(63)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ATerrestrialChannelOutsideUhfIsReported(int channel)
    {
        Assert.Contains(
            Request(physicalChannel: channel).Validate(Now),
            problem => problem.StartsWith("tuning.physicalChannel:")
        );
    }

    // The two plans do not share a numbering, so one range for both would let a
    // terrestrial channel number pass as a satellite one and fail down at the ioctl.
    [Theory]
    [InlineData(TunerKind.Satellite, 27)]
    [InlineData(TunerKind.Satellite, 25)]
    [InlineData(TunerKind.Terrestrial, 5)]
    public void AChannelFromTheOtherPlanIsReported(TunerKind kind, int channel)
    {
        Assert.Contains(
            Request(kind: kind, physicalChannel: channel).Validate(Now),
            problem => problem.StartsWith("tuning.physicalChannel:")
        );
    }

    [Fact]
    public void ASatelliteChannelInItsOwnPlanIsAccepted()
    {
        Assert.Empty(Request(kind: TunerKind.Satellite, physicalChannel: 15).Validate(Now));
    }

    // The name reaches the privileged process, so it is constrained the same way a
    // session id is rather than trusted to be used only as a lookup key.
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

    // A recording that carries an end already behind it would stop the moment it
    // started, which is worse than not starting: it books a tuner and writes nothing.
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

    // The recording has to be able to finish while the app is being replaced, which
    // it can only do if it knows when to stop.
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
