using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Carina.Contracts;

public static class DriverJson
{
    public static DriverJsonContext Context => DriverJsonContext.Default;

    public static string Serialize<T>(T value, JsonTypeInfo<T>? typeInfo = null) =>
        JsonSerializer.Serialize(value, typeInfo ?? Resolve<T>());

    public static T? Deserialize<T>(string json, JsonTypeInfo<T>? typeInfo = null) =>
        JsonSerializer.Deserialize(json, typeInfo ?? Resolve<T>());

    private static JsonTypeInfo<T> Resolve<T>() =>
        (JsonTypeInfo<T>)Context.GetTypeInfo(typeof(T))!;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
)]
[JsonSerializable(typeof(DriverHello))]
[JsonSerializable(typeof(StartSessionRequest))]
[JsonSerializable(typeof(TuningRequest))]
[JsonSerializable(typeof(SessionSnapshot))]
[JsonSerializable(typeof(TunerSnapshot))]
[JsonSerializable(typeof(DiagnosticSnapshot))]
[JsonSerializable(typeof(IReadOnlyList<SessionSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<TunerSnapshot>))]
[JsonSerializable(typeof(IReadOnlyList<DiagnosticSnapshot>))]
[JsonSerializable(typeof(SessionPurpose))]
[JsonSerializable(typeof(TunerKind))]
[JsonSerializable(typeof(TunerState))]
[JsonSerializable(typeof(SessionState))]
[JsonSerializable(typeof(DiagnosticReason))]
public sealed partial class DriverJsonContext : JsonSerializerContext;
