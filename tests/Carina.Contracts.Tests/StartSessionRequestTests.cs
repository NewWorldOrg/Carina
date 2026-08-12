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
    private static StartSessionRequest Request(
        SessionPurpose purpose = SessionPurpose.Live,
        TunerKind kind = TunerKind.Terrestrial,
        int physicalChannel = 27,
        int? serviceId = null,
        DateTimeOffset? endsAt = null
    ) =>
        new()
        {
            Purpose = purpose,
            Tuning = new TuningRequest(kind, physicalChannel, serviceId),
            EndsAt = endsAt,
        };

    [Fact]
    public void AnOrdinaryRequestHasNothingWrongWithIt()
    {
        Assert.Empty(Request().Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(63)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void APhysicalChannelOutsideTheBandIsReported(int channel)
    {
        Assert.Contains(
            Request(physicalChannel: channel).Validate(),
            problem => problem.StartsWith("tuning.physicalChannel:")
        );
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void AServiceIdOutsideSixteenBitsIsReported(int serviceId)
    {
        Assert.Contains(
            Request(serviceId: serviceId).Validate(),
            problem => problem.StartsWith("tuning.serviceId:")
        );
    }

    [Fact]
    public void APurposeThisDriverDoesNotKnowIsReported()
    {
        Assert.Contains(
            Request(purpose: SessionPurpose.Unspecified).Validate(),
            problem => problem.StartsWith("purpose:")
        );
    }

    [Fact]
    public void ATunerKindThisDriverDoesNotKnowIsReported()
    {
        Assert.Contains(
            Request(kind: TunerKind.Unspecified).Validate(),
            problem => problem.StartsWith("tuning.kind:")
        );
    }

    // The recording has to be able to finish while the app is being replaced, which
    // it can only do if it knows when to stop.
    [Fact]
    public void ARecordingWithoutAnEndTimeIsReported()
    {
        Assert.Contains(
            Request(purpose: SessionPurpose.Recording).Validate(),
            problem => problem.StartsWith("endsAt:")
        );
    }

    [Fact]
    public void ARecordingWithAnEndTimeIsAccepted()
    {
        Assert.Empty(
            Request(
                purpose: SessionPurpose.Recording,
                endsAt: new DateTimeOffset(2026, 8, 8, 22, 0, 0, TimeSpan.FromHours(9))
            ).Validate()
        );
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        var problems = Request(
            purpose: SessionPurpose.Unspecified,
            kind: TunerKind.Unspecified,
            physicalChannel: 0,
            serviceId: -5
        ).Validate();

        Assert.Equal(4, problems.Count);
    }
}
