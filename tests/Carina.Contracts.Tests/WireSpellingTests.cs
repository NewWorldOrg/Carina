namespace Carina.Contracts.Tests;

public sealed class WireSpellingTests
{
    [Theory]
    [InlineData(SessionPurpose.Unspecified, "unspecified")]
    [InlineData(SessionPurpose.Recording, "recording")]
    [InlineData(SessionPurpose.Live, "live")]
    [InlineData(SessionPurpose.Survey, "survey")]
    public void SessionPurposeIsSpelledThisWay(SessionPurpose value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.SessionPurpose);
    }

    [Theory]
    [InlineData(TuneSystem.Unspecified, "unspecified")]
    [InlineData(TuneSystem.IsdbT, "isdbT")]
    [InlineData(TuneSystem.IsdbSBs, "isdbSBs")]
    [InlineData(TuneSystem.IsdbSCs110, "isdbSCs110")]
    public void TuneSystemIsSpelledThisWay(TuneSystem value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.TuneSystem);
    }

    [Fact]
    public void TheSystemsAreExactlyTheThreeThatCanBeReceived()
    {
        Assert.Equal(
            new[]
            {
                TuneSystem.Unspecified,
                TuneSystem.IsdbT,
                TuneSystem.IsdbSBs,
                TuneSystem.IsdbSCs110,
            },
            Enum.GetValues<TuneSystem>()
        );
    }

    [Theory]
    [InlineData(TunerKind.Unspecified, "unspecified")]
    [InlineData(TunerKind.Terrestrial, "terrestrial")]
    [InlineData(TunerKind.Satellite, "satellite")]
    public void TunerKindIsSpelledThisWay(TunerKind value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.TunerKind);
    }

    [Theory]
    [InlineData(TunerState.Unspecified, "unspecified")]
    [InlineData(TunerState.Idle, "idle")]
    [InlineData(TunerState.Busy, "busy")]
    [InlineData(TunerState.Disabled, "disabled")]
    [InlineData(TunerState.Faulted, "faulted")]
    public void TunerStateIsSpelledThisWay(TunerState value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.TunerState);
    }

    [Theory]
    [InlineData(SessionState.Unspecified, "unspecified")]
    [InlineData(SessionState.Requested, "requested")]
    [InlineData(SessionState.Active, "active")]
    [InlineData(SessionState.Stopping, "stopping")]
    [InlineData(SessionState.Stopped, "stopped")]
    [InlineData(SessionState.Failed, "failed")]
    public void SessionStateIsSpelledThisWay(SessionState value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.SessionState);
    }

    [Theory]
    [InlineData(DiagnosticReason.Unspecified, "unspecified")]
    [InlineData(DiagnosticReason.RecordingWriteFailed, "recordingWriteFailed")]
    [InlineData(DiagnosticReason.DiskSpaceLow, "diskSpaceLow")]
    [InlineData(DiagnosticReason.DeviceFaulted, "deviceFaulted")]
    [InlineData(DiagnosticReason.TuningLost, "tuningLost")]
    [InlineData(DiagnosticReason.RecordingCutShort, "recordingCutShort")]
    [InlineData(DiagnosticReason.MeasurementFaulted, "measurementFaulted")]
    public void DiagnosticReasonIsSpelledThisWay(DiagnosticReason value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.DiagnosticReason);
    }

    [Theory]
    [InlineData(SessionStopReason.Unspecified, "unspecified")]
    [InlineData(SessionStopReason.Running, "running")]
    [InlineData(SessionStopReason.Requested, "requested")]
    [InlineData(SessionStopReason.EndTimeReached, "endTimeReached")]
    [InlineData(SessionStopReason.DrainCapReached, "drainCapReached")]
    [InlineData(SessionStopReason.DeviceFailed, "deviceFailed")]
    [InlineData(SessionStopReason.RecordingFailed, "recordingFailed")]
    public void SessionStopReasonIsSpelledThisWay(SessionStopReason value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.SessionStopReason);
    }

    [Fact]
    public void AStopReasonThisBuildDoesNotKnowIsNotMistakenForARequestedStop()
    {
        Assert.Equal(
            SessionStopReason.Unspecified,
            DriverJson.Deserialize("\"tunerStolen\"", DriverJson.Context.SessionStopReason)
        );
    }

    [Fact]
    public void TheStopReasonWireNameIsReadableWithoutSerializing()
    {
        Assert.Equal("requested", SessionStopReasonConverter.WireName(SessionStopReason.Requested));
        Assert.Equal(
            "drainCapReached",
            SessionStopReasonConverter.WireName(SessionStopReason.DrainCapReached)
        );
        Assert.Equal(
            "unspecified",
            SessionStopReasonConverter.WireName((SessionStopReason)99)
        );
    }

    [Fact]
    public void CapabilityNamesAreSpelledThisWay()
    {
        Assert.Equal("recording", DriverCapabilities.Recording);
        Assert.Equal("live", DriverCapabilities.Live);
        Assert.Equal("qualityMetering", DriverCapabilities.QualityMetering);
        Assert.Equal("descrambling", DriverCapabilities.Descrambling);
    }

    private static void AssertRoundTrip<T>(
        T value,
        string wire,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo
    )
    {
        Assert.Equal($"\"{wire}\"", DriverJson.Serialize(value, typeInfo));
        Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", typeInfo));
    }
}
