using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Contracts;

public abstract class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    protected abstract string NameOf(TEnum value);

    protected abstract TEnum? ValueOf(string name);

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var name = reader.GetString();
                return name is null ? default : ValueOf(name) ?? default;

            case JsonTokenType.Number:
            case JsonTokenType.Null:
            case JsonTokenType.True:
            case JsonTokenType.False:
                return default;

            default:
                if (!reader.TrySkip())
                {
                    throw new JsonException(
                        $"A structured value for {typeof(TEnum).Name} could not be consumed within one buffer."
                    );
                }

                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NameOf(value));
}

public sealed class SessionPurposeConverter : TolerantEnumConverter<SessionPurpose>
{
    protected override string NameOf(SessionPurpose value) =>
        value switch
        {
            SessionPurpose.Recording => "recording",
            SessionPurpose.Live => "live",
            SessionPurpose.Survey => "survey",
            SessionPurpose.Scan => "scan",
            _ => "unspecified",
        };

    protected override SessionPurpose? ValueOf(string name) =>
        name switch
        {
            "recording" => SessionPurpose.Recording,
            "live" => SessionPurpose.Live,
            "survey" => SessionPurpose.Survey,
            "scan" => SessionPurpose.Scan,
            "unspecified" => SessionPurpose.Unspecified,
            _ => null,
        };
}

public sealed class TuneSystemConverter : TolerantEnumConverter<TuneSystem>
{
    public static string WireName(TuneSystem value) =>
        value switch
        {
            TuneSystem.IsdbT => "isdbT",
            TuneSystem.IsdbSBs => "isdbSBs",
            TuneSystem.IsdbSCs110 => "isdbSCs110",
            _ => "unspecified",
        };

    protected override string NameOf(TuneSystem value) => WireName(value);

    protected override TuneSystem? ValueOf(string name) =>
        name switch
        {
            "isdbT" => TuneSystem.IsdbT,
            "isdbSBs" => TuneSystem.IsdbSBs,
            "isdbSCs110" => TuneSystem.IsdbSCs110,
            "unspecified" => TuneSystem.Unspecified,
            _ => null,
        };
}

public sealed class TunerKindConverter : TolerantEnumConverter<TunerKind>
{
    public static string WireName(TunerKind value) =>
        value switch
        {
            TunerKind.Terrestrial => "terrestrial",
            TunerKind.Satellite => "satellite",
            _ => "unspecified",
        };

    protected override string NameOf(TunerKind value) => WireName(value);

    protected override TunerKind? ValueOf(string name) =>
        name switch
        {
            "terrestrial" => TunerKind.Terrestrial,
            "satellite" => TunerKind.Satellite,
            "unspecified" => TunerKind.Unspecified,
            _ => null,
        };
}

public sealed class SignalLockConverter : TolerantEnumConverter<SignalLock>
{
    protected override string NameOf(SignalLock value) =>
        value switch
        {
            SignalLock.NotLocked => "notLocked",
            SignalLock.Locked => "locked",
            _ => "unspecified",
        };

    protected override SignalLock? ValueOf(string name) =>
        name switch
        {
            "notLocked" => SignalLock.NotLocked,
            "locked" => SignalLock.Locked,
            "unspecified" => SignalLock.Unspecified,
            _ => null,
        };
}

public sealed class TunerStateConverter : TolerantEnumConverter<TunerState>
{
    protected override string NameOf(TunerState value) =>
        value switch
        {
            TunerState.Idle => "idle",
            TunerState.Busy => "busy",
            TunerState.Disabled => "disabled",
            TunerState.Faulted => "faulted",
            TunerState.Draining => "draining",
            _ => "unspecified",
        };

    protected override TunerState? ValueOf(string name) =>
        name switch
        {
            "idle" => TunerState.Idle,
            "busy" => TunerState.Busy,
            "disabled" => TunerState.Disabled,
            "faulted" => TunerState.Faulted,
            "draining" => TunerState.Draining,
            "unspecified" => TunerState.Unspecified,
            _ => null,
        };
}

public sealed class TunerHealthLevelConverter : TolerantEnumConverter<TunerHealthLevel>
{
    protected override string NameOf(TunerHealthLevel value) =>
        value switch
        {
            TunerHealthLevel.Healthy => "healthy",
            TunerHealthLevel.Degraded => "degraded",
            TunerHealthLevel.Faulted => "faulted",
            _ => "unspecified",
        };

    protected override TunerHealthLevel? ValueOf(string name) =>
        name switch
        {
            "healthy" => TunerHealthLevel.Healthy,
            "degraded" => TunerHealthLevel.Degraded,
            "faulted" => TunerHealthLevel.Faulted,
            "unspecified" => TunerHealthLevel.Unspecified,
            _ => null,
        };
}

public sealed class DeviceDetectionConverter : TolerantEnumConverter<DeviceDetection>
{
    protected override string NameOf(DeviceDetection value) =>
        value switch
        {
            DeviceDetection.Detected => "detected",
            DeviceDetection.Busy => "busy",
            DeviceDetection.PermissionDenied => "permissionDenied",
            DeviceDetection.Unreadable => "unreadable",
            _ => "unspecified",
        };

    protected override DeviceDetection? ValueOf(string name) =>
        name switch
        {
            "detected" => DeviceDetection.Detected,
            "busy" => DeviceDetection.Busy,
            "permissionDenied" => DeviceDetection.PermissionDenied,
            "unreadable" => DeviceDetection.Unreadable,
            "unspecified" => DeviceDetection.Unspecified,
            _ => null,
        };
}

public sealed class SessionStateConverter : TolerantEnumConverter<SessionState>
{
    protected override string NameOf(SessionState value) =>
        value switch
        {
            SessionState.Requested => "requested",
            SessionState.Active => "active",
            SessionState.Stopping => "stopping",
            SessionState.Stopped => "stopped",
            SessionState.Failed => "failed",
            _ => "unspecified",
        };

    protected override SessionState? ValueOf(string name) =>
        name switch
        {
            "requested" => SessionState.Requested,
            "active" => SessionState.Active,
            "stopping" => SessionState.Stopping,
            "stopped" => SessionState.Stopped,
            "failed" => SessionState.Failed,
            "unspecified" => SessionState.Unspecified,
            _ => null,
        };
}

public sealed class SessionStopReasonConverter : TolerantEnumConverter<SessionStopReason>
{
    public static string WireName(SessionStopReason value) =>
        value switch
        {
            SessionStopReason.Running => "running",
            SessionStopReason.Requested => "requested",
            SessionStopReason.EndTimeReached => "endTimeReached",
            SessionStopReason.DrainCapReached => "drainCapReached",
            SessionStopReason.DeviceFailed => "deviceFailed",
            SessionStopReason.RecordingFailed => "recordingFailed",
            SessionStopReason.Preempted => "preempted",
            _ => "unspecified",
        };

    protected override string NameOf(SessionStopReason value) => WireName(value);

    protected override SessionStopReason? ValueOf(string name) =>
        name switch
        {
            "running" => SessionStopReason.Running,
            "requested" => SessionStopReason.Requested,
            "endTimeReached" => SessionStopReason.EndTimeReached,
            "drainCapReached" => SessionStopReason.DrainCapReached,
            "deviceFailed" => SessionStopReason.DeviceFailed,
            "recordingFailed" => SessionStopReason.RecordingFailed,
            "preempted" => SessionStopReason.Preempted,
            "unspecified" => SessionStopReason.Unspecified,
            _ => null,
        };
}

public sealed class DiagnosticReasonConverter : TolerantEnumConverter<DiagnosticReason>
{
    protected override string NameOf(DiagnosticReason value) =>
        value switch
        {
            DiagnosticReason.RecordingWriteFailed => "recordingWriteFailed",
            DiagnosticReason.DiskSpaceLow => "diskSpaceLow",
            DiagnosticReason.DeviceFaulted => "deviceFaulted",
            DiagnosticReason.TuningLost => "tuningLost",
            DiagnosticReason.RecordingCutShort => "recordingCutShort",
            DiagnosticReason.MeasurementFaulted => "measurementFaulted",
            _ => "unspecified",
        };

    protected override DiagnosticReason? ValueOf(string name) =>
        name switch
        {
            "recordingWriteFailed" => DiagnosticReason.RecordingWriteFailed,
            "diskSpaceLow" => DiagnosticReason.DiskSpaceLow,
            "deviceFaulted" => DiagnosticReason.DeviceFaulted,
            "tuningLost" => DiagnosticReason.TuningLost,
            "recordingCutShort" => DiagnosticReason.RecordingCutShort,
            "measurementFaulted" => DiagnosticReason.MeasurementFaulted,
            "unspecified" => DiagnosticReason.Unspecified,
            _ => null,
        };
}
