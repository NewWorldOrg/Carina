namespace Carina.Contracts.Tests;

public sealed class WireSpellingTests
{
    [Theory]
    [InlineData(SessionPurpose.Unspecified, "unspecified")]
    [InlineData(SessionPurpose.Recording, "recording")]
    [InlineData(SessionPurpose.Live, "live")]
    [InlineData(SessionPurpose.Survey, "survey")]
    [InlineData(SessionPurpose.Scan, "scan")]
    [InlineData(SessionPurpose.SurveyNow, "surveyNow")]
    public void SessionPurposeIsSpelledThisWay(SessionPurpose value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.SessionPurpose);
    }

    [Fact]
    public void EveryValueAddedToAnEnumIsGivenASpellingOfItsOwn()
    {
        AssertEveryValueIsSpelled(DriverJson.Context.SessionPurpose);
        AssertEveryValueIsSpelled(DriverJson.Context.TuneSystem);
        AssertEveryValueIsSpelled(DriverJson.Context.SignalLock);
        AssertEveryValueIsSpelled(DriverJson.Context.TunerHealthLevel);
        AssertEveryValueIsSpelled(DriverJson.Context.DeviceDetection);
        AssertEveryValueIsSpelled(DriverJson.Context.TunerKind);
        AssertEveryValueIsSpelled(DriverJson.Context.TunerState);
        AssertEveryValueIsSpelled(DriverJson.Context.SessionState);
        AssertEveryValueIsSpelled(DriverJson.Context.SessionStopReason);
        AssertEveryValueIsSpelled(DriverJson.Context.DiagnosticReason);
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
    [InlineData(SignalLock.Unspecified, "unspecified")]
    [InlineData(SignalLock.NotLocked, "notLocked")]
    [InlineData(SignalLock.Locked, "locked")]
    public void SignalLockIsSpelledThisWay(SignalLock value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.SignalLock);
    }

    [Theory]
    [InlineData(TunerHealthLevel.Unspecified, "unspecified")]
    [InlineData(TunerHealthLevel.Healthy, "healthy")]
    [InlineData(TunerHealthLevel.Degraded, "degraded")]
    [InlineData(TunerHealthLevel.Faulted, "faulted")]
    public void TunerHealthLevelIsSpelledThisWay(TunerHealthLevel value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.TunerHealthLevel);
    }

    [Theory]
    [InlineData(DeviceDetection.Unspecified, "unspecified")]
    [InlineData(DeviceDetection.Detected, "detected")]
    [InlineData(DeviceDetection.Busy, "busy")]
    [InlineData(DeviceDetection.PermissionDenied, "permissionDenied")]
    [InlineData(DeviceDetection.Unreadable, "unreadable")]
    public void DeviceDetectionIsSpelledThisWay(DeviceDetection value, string wire)
    {
        AssertRoundTrip(value, wire, DriverJson.Context.DeviceDetection);
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
    [InlineData(TunerState.Draining, "draining")]
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
    [InlineData(SessionStopReason.Preempted, "preempted")]
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
        Assert.Equal("signalQuality", DriverCapabilities.SignalQuality);
        Assert.Equal("liveTunerToggle", DriverCapabilities.LiveTunerToggle);
        Assert.Equal("typedTuning", DriverCapabilities.TypedTuning);
        Assert.Equal("deviceDetection", DriverCapabilities.DeviceDetection);
        Assert.Equal("tunerLedger", DriverCapabilities.TunerLedger);
        Assert.Equal("gracefulRestart", DriverCapabilities.GracefulRestart);
        Assert.Equal("ccMeasurement", DriverCapabilities.CcMeasurement);
        Assert.Equal("scrambleMeasurement", DriverCapabilities.ScrambleMeasurement);
        Assert.Equal("recordingExtension", DriverCapabilities.RecordingExtension);
        Assert.Equal("storage", DriverCapabilities.Storage);
    }

    [Fact]
    public void AMetricIsNamedUnderTheCapabilityItBelongsTo()
    {
        Assert.Equal(
            "signalQuality.cnr",
            DriverCapabilities.SignalQualityMetric(SignalQualityMetrics.Cnr)
        );
        Assert.Equal(
            "signalQuality.postViterbiBitError",
            DriverCapabilities.SignalQualityMetric(SignalQualityMetrics.PostViterbiBitError)
        );
        Assert.Equal("cnr", DriverCapabilities.MetricIn("signalQuality.cnr"));
        Assert.Null(DriverCapabilities.MetricIn("signalQuality"));
        Assert.Null(DriverCapabilities.MetricIn("signalQuality."));
        Assert.Null(DriverCapabilities.MetricIn("recording"));
    }

    private static void AssertEveryValueIsSpelled<T>(
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo
    )
        where T : struct, Enum
    {
        var spellings = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (T value in Enum.GetValues<T>())
        {
            string wire = DriverJson.Serialize(value, typeInfo).Trim('"');

            Assert.False(
                !EqualityComparer<T>.Default.Equals(value, default) && wire is "unspecified",
                $"{typeof(T).Name}.{value} has no spelling of its own."
            );
            Assert.False(
                spellings.TryGetValue(wire, out T taken),
                $"{typeof(T).Name}.{value} is spelled '{wire}', which {taken} already answers to."
            );
            Assert.Equal(value, DriverJson.Deserialize($"\"{wire}\"", typeInfo));

            spellings[wire] = value;
        }
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
