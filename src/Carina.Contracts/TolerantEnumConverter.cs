using System.Text.Json;
using System.Text.Json.Serialization;

namespace Carina.Contracts;

/// <summary>
/// Reads an enum by its pinned name, and reads anything else as the zero value.
/// </summary>
/// <remarks>
/// Enum values grow: the priority ladder alone already needs more session purposes
/// than exist today. A driver may therefore report a name an older app has never
/// heard of, and the answer has to stay readable — the contract promises additive
/// change and no exceptions, so an unknown name degrades to the type's "unspecified"
/// member instead of failing the whole message. Every such enum keeps that member at
/// zero, which also makes an absent field read as "not stated" rather than as
/// whichever member happened to be declared first.
///
/// Numbers are read the same way. An ordinal carries no name to check, so honouring
/// it would land a future value on today's member — silently, and wrongly.
///
/// The spellings are written out in each converter rather than derived from the
/// members, because the driver is published ahead of time and cannot read its own
/// metadata at runtime.
/// </remarks>
public abstract class TolerantEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    /// <summary>The wire spelling of <paramref name="value"/>.</summary>
    protected abstract string NameOf(TEnum value);

    /// <summary>The member <paramref name="name"/> spells, if this build knows it.</summary>
    protected abstract TEnum? ValueOf(string name);

    /// <inheritdoc />
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            reader.Skip();
            return default;
        }

        var name = reader.GetString();
        return name is null ? default : ValueOf(name) ?? default;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NameOf(value));
}

/// <summary>Wire spelling of <see cref="SessionPurpose"/>.</summary>
public sealed class SessionPurposeConverter : TolerantEnumConverter<SessionPurpose>
{
    /// <inheritdoc />
    protected override string NameOf(SessionPurpose value) =>
        value switch
        {
            SessionPurpose.Recording => "recording",
            SessionPurpose.Live => "live",
            SessionPurpose.Survey => "survey",
            _ => "unspecified",
        };

    /// <inheritdoc />
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

/// <summary>Wire spelling of <see cref="TunerKind"/>.</summary>
public sealed class TunerKindConverter : TolerantEnumConverter<TunerKind>
{
    /// <inheritdoc />
    protected override string NameOf(TunerKind value) =>
        value switch
        {
            TunerKind.Terrestrial => "terrestrial",
            TunerKind.Satellite => "satellite",
            _ => "unspecified",
        };

    /// <inheritdoc />
    protected override TunerKind? ValueOf(string name) =>
        name switch
        {
            "terrestrial" => TunerKind.Terrestrial,
            "satellite" => TunerKind.Satellite,
            "unspecified" => TunerKind.Unspecified,
            _ => null,
        };
}

/// <summary>Wire spelling of <see cref="TunerState"/>.</summary>
public sealed class TunerStateConverter : TolerantEnumConverter<TunerState>
{
    /// <inheritdoc />
    protected override string NameOf(TunerState value) =>
        value switch
        {
            TunerState.Idle => "idle",
            TunerState.Busy => "busy",
            TunerState.Disabled => "disabled",
            TunerState.Faulted => "faulted",
            _ => "unspecified",
        };

    /// <inheritdoc />
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

/// <summary>Wire spelling of <see cref="SessionState"/>.</summary>
public sealed class SessionStateConverter : TolerantEnumConverter<SessionState>
{
    /// <inheritdoc />
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

    /// <inheritdoc />
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

/// <summary>Wire spelling of <see cref="DiagnosticReason"/>.</summary>
public sealed class DiagnosticReasonConverter : TolerantEnumConverter<DiagnosticReason>
{
    /// <inheritdoc />
    protected override string NameOf(DiagnosticReason value) =>
        value switch
        {
            DiagnosticReason.RecordingWriteFailed => "recordingWriteFailed",
            DiagnosticReason.DiskSpaceLow => "diskSpaceLow",
            DiagnosticReason.DeviceFaulted => "deviceFaulted",
            DiagnosticReason.TuningLost => "tuningLost",
            _ => "unspecified",
        };

    /// <inheritdoc />
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
