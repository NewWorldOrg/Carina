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
            _ => "unspecified",
        };

    protected override SessionPurpose? ValueOf(string name) =>
        name switch
        {
            "recording" => SessionPurpose.Recording,
            "live" => SessionPurpose.Live,
            "survey" => SessionPurpose.Survey,
            "unspecified" => SessionPurpose.Unspecified,
            _ => null,
        };
}

public sealed class TunerKindConverter : TolerantEnumConverter<TunerKind>
{
    protected override string NameOf(TunerKind value) =>
        value switch
        {
            TunerKind.Terrestrial => "terrestrial",
            TunerKind.Satellite => "satellite",
            _ => "unspecified",
        };

    protected override TunerKind? ValueOf(string name) =>
        name switch
        {
            "terrestrial" => TunerKind.Terrestrial,
            "satellite" => TunerKind.Satellite,
            "unspecified" => TunerKind.Unspecified,
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
            _ => "unspecified",
        };

    protected override TunerState? ValueOf(string name) =>
        name switch
        {
            "idle" => TunerState.Idle,
            "busy" => TunerState.Busy,
            "disabled" => TunerState.Disabled,
            "faulted" => TunerState.Faulted,
            "unspecified" => TunerState.Unspecified,
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

public sealed class DiagnosticReasonConverter : TolerantEnumConverter<DiagnosticReason>
{
    protected override string NameOf(DiagnosticReason value) =>
        value switch
        {
            DiagnosticReason.RecordingWriteFailed => "recordingWriteFailed",
            DiagnosticReason.DiskSpaceLow => "diskSpaceLow",
            DiagnosticReason.DeviceFaulted => "deviceFaulted",
            DiagnosticReason.TuningLost => "tuningLost",
            _ => "unspecified",
        };

    protected override DiagnosticReason? ValueOf(string name) =>
        name switch
        {
            "recordingWriteFailed" => DiagnosticReason.RecordingWriteFailed,
            "diskSpaceLow" => DiagnosticReason.DiskSpaceLow,
            "deviceFaulted" => DiagnosticReason.DeviceFaulted,
            "tuningLost" => DiagnosticReason.TuningLost,
            "unspecified" => DiagnosticReason.Unspecified,
            _ => null,
        };
}
